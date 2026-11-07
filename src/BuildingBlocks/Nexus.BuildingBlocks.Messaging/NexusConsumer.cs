using Avro.Specific;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Messaging.Inbox;
using Nexus.BuildingBlocks.Messaging.Retry;

namespace Nexus.BuildingBlocks.Messaging;

/// <summary>
/// Base Kafka consumer (conventions-events.md §7). Per message:
/// <list type="number">
///   <item>The record is read as RAW bytes (key UTF-8, value <c>byte[]</c>), so a message that cannot
///   be deserialized can still be forwarded byte-for-byte to the DLQ.</item>
///   <item>1 attempt + 3 retries (<see cref="Retry.RetryOptions.MaxAttempts"/> = 4, back-off 200 ms / 1 s / 5 s),
///   each one: Avro deserialization (<see cref="AvroDeserializer{T}"/> against the Schema Registry) →
///   <see cref="EventEnvelope.FromHeaders"/> → a NEW DI scope → the inbox processor
///   under <see cref="ConsumerOptions.Identity"/> → <see cref="HandleAsync"/> inside that transaction.</item>
///   <item>If every attempt fails (handler, poison bytes or a broken envelope alike), the original
///   record goes to <c>{topic}.DLQ</c> with the <c>nexus-dlq-*</c> headers.</item>
///   <item>Only then — success, already processed, or acknowledged by the DLQ — is the offset committed
///   (<c>enable.auto.commit=false</c>), and the loop moves on.</item>
/// </list>
/// <para>
/// <b>Cancellation</b> (host shutdown) is never retried, never sent to the DLQ and never commits: the
/// message is delivered again after a restart and the inbox makes that harmless.
/// </para>
/// <para>
/// <b>DLQ unavailable.</b> If the DLQ publish fails (it gives up after
/// <see cref="NexusKafkaOptions.DlqMessageTimeout"/>), the record is NOT skipped and the host is NOT
/// stopped: the loop pauses every assigned partition, reports
/// <see cref="ConsumerLoopState.DlqUnavailable"/> (Unhealthy) and retries the publish with an exponential
/// back-off (<see cref="Retry.RetryOptions.DlqPublishInitialBackoff"/> doubling up to
/// <see cref="Retry.RetryOptions.DlqPublishMaxBackoff"/>, ~30 s). While it waits it keeps polling the
/// paused consumer (no record is delivered; the poll keeps the group membership alive and serves
/// rebalances; a record of a partition assigned meanwhile is rewound and paused, never processed). When
/// the publish succeeds the offset is committed, the partitions are resumed and the state returns to
/// <see cref="ConsumerLoopState.Running"/>.
/// </para>
/// <para>
/// <b>Poll errors.</b> A non-fatal error from the poll itself (unknown topic, broker unreachable...) is
/// logged without payload, reported as <see cref="ConsumerLoopState.Degraded"/> and retried after a
/// short back-off (1 s doubling to 5 s); the loop goes on and returns to Running on the next good poll.
/// Only a FATAL Kafka error (or a bug) ends the loop: it reports <see cref="ConsumerLoopState.Faulted"/>
/// and rethrows, so the host stops (<c>BackgroundServiceExceptionBehavior.StopHost</c>) and its
/// orchestrator restarts it.
/// </para>
/// <para>
/// <b>Time budget.</b> <c>max.poll.interval.ms</c> is set explicitly from
/// <see cref="ConsumerOptions.MaxPollInterval"/> (5 min); see <see cref="NexusKafkaOptions"/> for how a
/// record's worst case fits in it.
/// </para>
/// </summary>
public abstract partial class NexusConsumer<TEvent> : BackgroundService
    where TEvent : class, ISpecificRecord
{
    private static readonly TimeSpan PollErrorInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollErrorMaxBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepAlivePollTimeout = TimeSpan.FromMilliseconds(100);

    private readonly NexusConsumerRuntime _runtime;
    private readonly RetryPolicy _retry;
    private readonly ILogger _logger;
    private IAsyncDeserializer<TEvent>? _deserializer;

    /// <summary>The loop's consumer while it runs (only touched from the loop's own flow).</summary>
    private IConsumer<string?, byte[]?>? _consumer;

    protected NexusConsumer(ConsumerOptions options, NexusConsumerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);
        Options = options;
        _runtime = runtime;
        _retry = new RetryPolicy(runtime.RetryOptions, runtime.TimeProvider);
        _logger = runtime.LoggerFactory.CreateLogger(GetType());
        runtime.Health.Register(options.GroupId, options.Topic);
    }

    protected ConsumerOptions Options { get; }

    /// <summary>
    /// The effect of one event. Runs inside the message's unit of work, already begun by the inbox
    /// with <c>app.current_user_id</c> per <see cref="ConsumerOptions.Identity"/> and
    /// <c>app.correlation_id</c> = the event's correlation id.
    /// <para>
    /// Resolve EVERY service from <paramref name="services"/> (the message's DI scope): that is the
    /// scope whose <c>IUnitOfWork</c> holds the open transaction. Services captured in the consumer's
    /// constructor (a singleton) would write outside it.
    /// </para>
    /// <para>
    /// Return normally only when the effect is done: the inbox then commits it together with the
    /// <c>inbox.processed_message</c> row and any outbox rows. To signal failure, THROW. In particular,
    /// an effect that dispatches a command handler must turn a failed <c>Result</c> into an exception
    /// (e.g. <c>throw new InvalidOperationException(result.Error.ToString())</c>); returning normally
    /// would commit whatever the handler wrote before failing and the event would never be retried.
    /// </para>
    /// </summary>
    protected abstract Task HandleAsync(TEvent message, EventEnvelope envelope, IServiceProvider services, CancellationToken ct);

    /// <summary>
    /// Retry + inbox + DLQ around one record. Returns normally when the offset may be committed
    /// (processed, already processed, or forwarded to the DLQ — retrying that publish, paused, for as
    /// long as it takes). Throws only on cancellation; the offset must not be committed then.
    /// </summary>
    protected internal virtual Task ProcessAsync(ConsumeResult<string?, byte[]?> result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        var attempt = 0;
        return _retry.ExecuteWithRetryAsync(
            async token =>
            {
                attempt++;
                try
                {
                    await ProcessAttemptAsync(result, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    LogAttemptFailed(_logger, ex, Options.GroupId, result.Topic, result.Partition.Value, result.Offset.Value, attempt);
                    throw;
                }
            },
            failure => DeadLetterAsync(result, failure, ct),
            ct);
    }

    /// <summary>
    /// Publishes the record to its DLQ. On failure: pause, report Unhealthy, back off (TimeProvider)
    /// while keeping the consumer polled, retry — until it succeeds or <paramref name="ct"/> is cancelled.
    /// </summary>
    private async Task DeadLetterAsync(ConsumeResult<string?, byte[]?> result, Exception failure, CancellationToken ct)
    {
        var backoff = _runtime.RetryOptions.DlqPublishInitialBackoff;
        var failed = false;
        var paused = false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _runtime.DlqPublisher.PublishAsync(
                    result.Topic,
                    result.Message.Key,
                    result.Message.Value,
                    result.Message.Headers,
                    failure,
                    Options.GroupId,
                    ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                LogDlqPublishFailed(_logger, ex, Options.GroupId, result.Topic, result.Partition.Value, result.Offset.Value, attempt, backoff);
                failed = true;
                _runtime.Health.Report(Options.GroupId, ConsumerLoopState.DlqUnavailable, ex);
                if (!paused && _consumer is { } consumer)
                {
                    consumer.Pause(consumer.Assignment);
                    paused = true;
                }

                await WaitKeepingConsumerAliveAsync(backoff, ct).ConfigureAwait(false);
                backoff = Next(backoff, _runtime.RetryOptions.DlqPublishMaxBackoff);
            }
        }

        LogSentToDlq(_logger, failure, Options.GroupId, result.Topic, result.Partition.Value, result.Offset.Value);
        if (failed)
        {
            if (paused && _consumer is { } c)
            {
                // The current assignment: partitions revoked meanwhile are gone, new ones were paused.
                c.Resume(c.Assignment);
            }

            _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Running);
            LogResumed(_logger, Options.GroupId, result.Topic);
        }
    }

    /// <summary>
    /// Waits <paramref name="delay"/> (TimeProvider) while polling the PAUSED consumer, so the group
    /// membership survives <c>max.poll.interval.ms</c> and rebalances are served. A paused consumer
    /// returns no records; a record that still shows up (a partition assigned during the pause) is
    /// rewound and its partition paused, so it is consumed after the resume, never skipped.
    /// </summary>
    private async Task WaitKeepingConsumerAliveAsync(TimeSpan delay, CancellationToken ct)
    {
        var wait = Task.Delay(delay, _runtime.TimeProvider, ct);
        if (_consumer is not { } consumer)
        {
            await wait.ConfigureAwait(false);
            return;
        }

        while (!wait.IsCompleted)
        {
            try
            {
                var stray = consumer.Consume(KeepAlivePollTimeout);
                if (stray is { IsPartitionEOF: false, Message: not null })
                {
                    consumer.Seek(stray.TopicPartitionOffset);
                    consumer.Pause([stray.TopicPartition]);
                }
            }
            catch (ConsumeException e) when (!e.Error.IsFatal)
            {
                // The broker is likely down too (that is why the DLQ failed): keep waiting.
                LogPollError(_logger, Options.GroupId, Options.Topic, e.Error.Code, e.Error.Reason);
            }
        }

        await wait.ConfigureAwait(false); // throws if cancelled
    }

    private static TimeSpan Next(TimeSpan current, TimeSpan max)
    {
        var doubled = current * 2;
        return doubled > max ? max : doubled;
    }

    private async Task ProcessAttemptAsync(ConsumeResult<string?, byte[]?> result, CancellationToken ct)
    {
        var deserializer = _deserializer
            ?? throw new InvalidOperationException("The consumer loop has not created its Avro deserializer yet.");
        var raw = result.Message.Value;
        var message = await deserializer.DeserializeAsync(
            raw ?? ReadOnlyMemory<byte>.Empty,
            raw is null,
            new SerializationContext(MessageComponentType.Value, result.Topic, result.Message.Headers)).ConfigureAwait(false);
        if (message is null)
        {
            throw new InvalidOperationException("The record has no value (tombstone); NEXUS events always carry a payload.");
        }

        var envelope = EventEnvelope.FromHeaders(result.Message.Headers ?? new Headers());

        // One DI scope per attempt: the inbox processor and the unit of work it drives are
        // single-use, and a failed attempt's scope (rolled back) must not leak into the next one.
        var scope = _runtime.ScopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            // Never resolved from DI (ADR-0017): only the consumer can switch a scope to the event's identity.
            var inbox = ActivatorUtilities.CreateInstance<InboxProcessor>(services);
            var outcome = await inbox.ProcessOnceAsync(
                Options.GroupId,
                envelope,
                Options.Identity,
                token => HandleAsync(message, envelope, services, token),
                ct).ConfigureAwait(false);
            if (outcome == InboxResult.AlreadyProcessed)
            {
                LogDuplicate(_logger, Options.GroupId, envelope.EventId);
            }
        }
    }

    /// <summary>Raw-bytes consumer: key UTF-8, value untouched (deserialized per attempt, see <see cref="ProcessAsync"/>).</summary>
    protected virtual IConsumer<string?, byte[]?> BuildConsumer() =>
        new ConsumerBuilder<string?, byte[]?>(CreateConsumerConfig()).Build();

    /// <summary>The librdkafka settings of <see cref="BuildConsumer"/>.</summary>
    internal ConsumerConfig CreateConsumerConfig() =>
        new()
        {
            BootstrapServers = Options.BootstrapServers,
            GroupId = Options.GroupId,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            MaxPollIntervalMs = (int)Options.MaxPollInterval.TotalMilliseconds,
        };

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), CancellationToken.None);

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var registry = new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = Options.SchemaRegistryUrl });
            _deserializer = new AvroDeserializer<TEvent>(registry);
            using var consumer = BuildConsumer();
            _consumer = consumer;
            try
            {
                consumer.Subscribe(Options.Topic);
                _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Running);
                LogSubscribed(_logger, Options.GroupId, Options.Topic);

                var pollBackoff = PollErrorInitialBackoff;
                var degraded = false;
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string?, byte[]?>? result;
                    try
                    {
                        result = consumer.Consume(stoppingToken);
                    }
                    catch (ConsumeException e) when (!e.Error.IsFatal)
                    {
                        // Transient (unknown topic, broker unreachable, ...): never stop the host for it.
                        // Logged without the record: only the error code and reason.
                        LogPollError(_logger, Options.GroupId, Options.Topic, e.Error.Code, e.Error.Reason);
                        if (!degraded)
                        {
                            _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Degraded);
                            degraded = true;
                        }

                        await Task.Delay(pollBackoff, _runtime.TimeProvider, stoppingToken).ConfigureAwait(false);
                        pollBackoff = Next(pollBackoff, PollErrorMaxBackoff);
                        continue;
                    }

                    if (degraded)
                    {
                        _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Running);
                        degraded = false;
                        pollBackoff = PollErrorInitialBackoff;
                    }

                    if (result is null || result.IsPartitionEOF || result.Message is null)
                    {
                        continue;
                    }

                    await ProcessAsync(result, stoppingToken).ConfigureAwait(false);
                    Commit(consumer, result);
                }
            }
            finally
            {
                _consumer = null;
                consumer.Close();
            }

            _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Stopped);
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown: whatever was in flight is not committed and will be redelivered.
            _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Stopped);
        }
        catch (Exception ex)
        {
            _runtime.Health.Report(Options.GroupId, ConsumerLoopState.Faulted, ex);
            LogLoopDied(_logger, ex, Options.GroupId, Options.Topic);
            throw;
        }
    }

    private void Commit(IConsumer<string?, byte[]?> consumer, ConsumeResult<string?, byte[]?> result)
    {
        try
        {
            consumer.Commit(result);
        }
        catch (KafkaException ex)
        {
            // E.g. the partition was revoked by a rebalance. The effect is already durable; the record
            // is redelivered to whoever owns the partition and the inbox skips it. Commits are
            // cumulative, so the next successful one covers this offset if we keep the partition.
            LogCommitFailed(_logger, ex, Options.GroupId, result.Topic, result.Partition.Value, result.Offset.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer {GroupId} subscribed to {Topic}")]
    private static partial void LogSubscribed(ILogger logger, string groupId, string topic);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Consumer {GroupId}: attempt {Attempt} failed for {Topic}[{Partition}]@{Offset}")]
    private static partial void LogAttemptFailed(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset, int attempt);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Consumer {GroupId}: {Topic}[{Partition}]@{Offset} exhausted its retries and was sent to the DLQ")]
    private static partial void LogSentToDlq(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Consumer {GroupId}: event {EventId} already processed, skipped")]
    private static partial void LogDuplicate(ILogger logger, string groupId, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Consumer {GroupId}: offset commit failed for {Topic}[{Partition}]@{Offset}; the record will be redelivered and skipped by the inbox")]
    private static partial void LogCommitFailed(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Consumer {GroupId} on {Topic}: poll failed with {ErrorCode} ({Reason}); retrying")]
    private static partial void LogPollError(ILogger logger, string groupId, string topic, ErrorCode errorCode, string reason);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Consumer {GroupId}: DLQ publish #{Attempt} of {Topic}[{Partition}]@{Offset} failed; consumption paused, retrying in {Backoff}")]
    private static partial void LogDlqPublishFailed(ILogger logger, Exception ex, string groupId, string topic, int partition, long offset, int attempt, TimeSpan backoff);

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumer {GroupId}: DLQ reachable again, consumption of {Topic} resumed")]
    private static partial void LogResumed(ILogger logger, string groupId, string topic);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Consumer {GroupId} on {Topic} stopped with a fatal error; nothing is consumed for this group")]
    private static partial void LogLoopDied(ILogger logger, Exception ex, string groupId, string topic);
}

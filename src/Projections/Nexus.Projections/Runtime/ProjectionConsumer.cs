using Avro.Generic;
using Confluent.Kafka;
using Confluent.SchemaRegistry.Serdes;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Runtime;

/// <summary>
/// The long-running consumer of ONE projection (group <c>projections.&lt;name&gt;</c>): subscribes (regex or
/// topic list), polls, hands every record to its <see cref="ProjectionPipeline"/> and flushes when the
/// batch is due. Loop semantics copied from <see cref="Nexus.BuildingBlocks.Messaging.NexusConsumer{TEvent}"/>:
/// <list type="bullet">
///   <item>A non-fatal poll error (unknown topic, broker unreachable...) is logged without payload,
///   reported as <see cref="ConsumerLoopState.Degraded"/> and retried after 1 s doubling to 5 s; the loop
///   is Running again on the next record, or after <see cref="DegradedQuietPeriod"/> without errors with
///   partitions assigned.</item>
///   <item>A FATAL Kafka error (or a bug) ends the loop: <see cref="ConsumerLoopState.Faulted"/> + rethrow,
///   so the host stops (<c>BackgroundServiceExceptionBehavior.StopHost</c>) and its orchestrator restarts it.</item>
///   <item>Cancellation (host shutdown) never flushes nor commits: the buffered records are redelivered.</item>
/// </list>
/// </summary>
public sealed partial class ProjectionConsumer
{
    /// <summary>Upper bound of one poll; an idle topic still returns control at least this often.</summary>
    internal static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Error-free time (with partitions assigned) after which a Degraded loop is Running again.</summary>
    internal static readonly TimeSpan DegradedQuietPeriod = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan PollErrorInitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollErrorMaxBackoff = TimeSpan.FromSeconds(5);

    private readonly IProjection _projection;
    private readonly ProjectionRuntime _runtime;
    private readonly ILogger _logger;

    public ProjectionConsumer(IProjection projection, ProjectionRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(runtime);
        _projection = projection;
        _runtime = runtime;
        _logger = runtime.LoggerFactory.CreateLogger<ProjectionConsumer>();
        GroupId = ProjectionCatalog.GroupId(projection);
        runtime.Health.Register(GroupId, ProjectionCatalog.SubscriptionLabel(projection));
    }

    public string GroupId { get; }

    /// <summary>Runs until <paramref name="stoppingToken"/> is cancelled (returns) or a fatal error (throws).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            var deserializer = new AvroDeserializer<GenericRecord>(_runtime.SchemaRegistry);
            ProjectionPipeline? pipeline = null;
            using var consumer = new ConsumerBuilder<string?, byte[]?>(_runtime.CreateConsumerConfig(_projection))
                .SetPartitionsRevokedHandler((_, partitions) => pipeline?.OnPartitionsRevoked(partitions.Select(p => p.TopicPartition)))
                .SetPartitionsLostHandler((_, partitions) => pipeline?.OnPartitionsRevoked(partitions.Select(p => p.TopicPartition)))
                .Build();
            pipeline = new ProjectionPipeline(_projection, _runtime, consumer, deserializer, commitFailureIsFatal: false);
            try
            {
                Subscribe(consumer);
                _runtime.Health.Report(GroupId, ConsumerLoopState.Running);
                await LoopAsync(consumer, pipeline, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                // Close leaves the group (revocation drops the uncommitted buffer: nothing is committed).
                consumer.Close();
            }

            _runtime.Health.Report(GroupId, ConsumerLoopState.Stopped);
        }
        catch (Exception) when (stoppingToken.IsCancellationRequested)
        {
            _runtime.Health.Report(GroupId, ConsumerLoopState.Stopped);
        }
        catch (Exception ex)
        {
            _runtime.Health.Report(GroupId, ConsumerLoopState.Faulted, ex);
            LogLoopDied(_logger, ex, GroupId);
            throw;
        }
    }

    private void Subscribe(IConsumer<string?, byte[]?> consumer)
    {
        var subscription = _projection.Subscription;
        if (subscription.Pattern is { } pattern)
        {
            consumer.Subscribe(pattern); // a topic string starting with '^' is a librdkafka regex
        }
        else
        {
            consumer.Subscribe(subscription.Topics!);
        }

        LogSubscribed(_logger, GroupId, ProjectionCatalog.SubscriptionLabel(_projection));
    }

    private async Task LoopAsync(IConsumer<string?, byte[]?> consumer, ProjectionPipeline pipeline, CancellationToken stoppingToken)
    {
        var time = _runtime.TimeProvider;
        var pollBackoff = PollErrorInitialBackoff;
        var degraded = false;
        var lastPollError = 0L;
        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string?, byte[]?>? result;
            try
            {
                result = consumer.Consume(pipeline.TimeUntilFlush(PollTimeout));
            }
            catch (ConsumeException e) when (!e.Error.IsFatal)
            {
                _runtime.Metrics.Error(_projection.Name, ProjectionMetrics.ErrorKindPoll);
                LogPollError(_logger, GroupId, e.Error.Code, e.Error.Reason);
                lastPollError = time.GetTimestamp();
                if (!degraded)
                {
                    _runtime.Health.Report(GroupId, ConsumerLoopState.Degraded);
                    degraded = true;
                }

                // The batch keeps its deadline: flush what is due before backing off.
                if (pipeline.FlushDue)
                {
                    await pipeline.FlushAsync(stoppingToken).ConfigureAwait(false);
                }

                await Task.Delay(pollBackoff, time, stoppingToken).ConfigureAwait(false);
                pollBackoff = ProjectionRuntime.NextBackoff(pollBackoff, PollErrorMaxBackoff);
                continue;
            }

            if (degraded
                && (result is { IsPartitionEOF: false, Message: not null }
                    || (consumer.Assignment.Count > 0 && time.GetElapsedTime(lastPollError) >= DegradedQuietPeriod)))
            {
                _runtime.Health.Report(GroupId, ConsumerLoopState.Running);
                degraded = false;
                pollBackoff = PollErrorInitialBackoff;
            }

            if (result is { IsPartitionEOF: false, Message: not null })
            {
                await pipeline.ProcessAsync(result, stoppingToken).ConfigureAwait(false);
            }

            if (pipeline.FlushDue)
            {
                await pipeline.FlushAsync(stoppingToken).ConfigureAwait(false);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Projection consumer {GroupId} subscribed to {Subscription}")]
    private static partial void LogSubscribed(ILogger logger, string groupId, string subscription);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Projection consumer {GroupId}: poll failed with {ErrorCode} ({Reason}); retrying")]
    private static partial void LogPollError(ILogger logger, string groupId, ErrorCode errorCode, string reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Projection consumer {GroupId} stopped with a fatal error; nothing is projected for this group")]
    private static partial void LogLoopDied(ILogger logger, Exception ex, string groupId);
}

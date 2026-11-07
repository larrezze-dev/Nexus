using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nexus.OutboxRelay.Publishing;

/// <summary>
/// Produces one outbox row to Kafka (conventions-events.md §4.4): idempotent producer
/// (<c>enable.idempotence=true</c>, <c>acks=all</c>), key = <c>aggregate_id</c>, value = Confluent
/// wire-format Avro, headers per §2.
///
/// <para>
/// <b>One message at a time, delivery report awaited.</b> The relay awaits each message's
/// acknowledgement before producing the next one. A pipelined producer keeps per-partition order
/// for messages that all succeed, but when message n of an aggregate fails (e.g. times out) after
/// n+1 has already been handed to librdkafka, n+1 can be delivered while n is recorded as failed and
/// retried later — n would then land after n+1 (EV-R3 broken). Awaiting each report makes "n not
/// acknowledged ⇒ n+1 never sent" trivially true. The cost is one broker round trip per message;
/// if B6 shows it is too slow, the safe evolution is pipelining across <i>different</i> aggregates
/// only, never within one.
/// </para>
///
/// <para>
/// The produce call is not cancelled by the shutdown token: once handed to librdkafka the message
/// may be delivered regardless, so the relay waits for the outcome (bounded by
/// <see cref="RelayOptions.ProduceTimeoutMs"/>) and records it.
/// </para>
///
/// <para>
/// <b>Topic preflight.</b> With <c>auto.create.topics.enable=false</c> a produce to a missing topic
/// is not refused: librdkafka keeps it queued and reports <c>Local_MsgTimedOut</c> only after
/// <see cref="RelayOptions.ProduceTimeoutMs"/> (30 s), inside the batch transaction, and that
/// error is indistinguishable from a cluster outage. So before producing, the topic's metadata is
/// checked (bounded by <see cref="RelayOptions.TopicPreflightTimeoutMs"/>, cached per topic with
/// <see cref="TimeProvider"/>: <see cref="PositiveTopicTtl"/> when it exists, <see cref="NegativeTopicTtl"/>
/// when not). A missing or unauthorised topic is <see cref="PublishFailureKind.TopicUnreachable"/>,
/// counted as an attempt only when the cluster was actually asked (the preflight ran or the produce
/// answered); while negatively cached the row is <see cref="PublishResult.Deferred"/> (not counted);
/// a metadata request that cannot reach the cluster is <see cref="PublishFailureKind.InfraGlobal"/>.
/// </para>
///
/// <para>
/// The producer is created lazily, on the first publish (i.e. once the instance is leader), so a
/// passive instance holds no Kafka connection and logs no client errors.
/// </para>
/// </summary>
public sealed partial class KafkaEventPublisher : IEventPublisher, IDisposable
{
    /// <summary>How long a successful topic preflight is trusted.</summary>
    public static readonly TimeSpan PositiveTopicTtl = TimeSpan.FromSeconds(30);

    /// <summary>How long a missing/unauthorised topic is remembered before asking the cluster again.</summary>
    public static readonly TimeSpan NegativeTopicTtl = TimeSpan.FromSeconds(5);

    // The topic does not exist or the relay may not write to it: TopicUnreachable.
    private static readonly HashSet<ErrorCode> TopicErrors =
    [
        ErrorCode.UnknownTopicOrPart,
        ErrorCode.Local_UnknownTopic,
        ErrorCode.Local_UnknownPartition,
        ErrorCode.TopicAuthorizationFailed,
    ];

    // Problems with this message itself: Row (FAILED at MaxAttempts).
    private static readonly HashSet<ErrorCode> RowErrors =
    [
        ErrorCode.MsgSizeTooLarge,
        ErrorCode.RecordListTooLarge,
        ErrorCode.InvalidRecord,
        ErrorCode.TopicException,
        ErrorCode.Local_KeySerialization,
        ErrorCode.Local_ValueSerialization,
    ];

    private readonly IPayloadEncoder _encoder;
    private readonly ProducerConfig _config;
    private readonly TimeSpan _preflightTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private readonly Dictionary<string, (string? Error, DateTimeOffset Until)> _topics = new(StringComparer.Ordinal);
    private IProducer<string, byte[]>? _producer;
    private IAdminClient? _admin;

    public KafkaEventPublisher(IPayloadEncoder encoder, IOptions<RelayOptions> options, TimeProvider timeProvider, ILogger<KafkaEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _encoder = encoder;
        _timeProvider = timeProvider;
        _logger = logger;
        _config = CreateProducerConfig(options.Value);
        _preflightTimeout = TimeSpan.FromMilliseconds(options.Value.TopicPreflightTimeoutMs);
    }

    /// <summary>Whether the Kafka producer has been created (it is, lazily, on the first publish).</summary>
    internal bool ProducerCreated => _producer is not null;

    public static ProducerConfig CreateProducerConfig(RelayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ProducerConfig
        {
            BootstrapServers = options.KafkaBootstrapServers,
            ClientId = "nexus-relay",
            EnableIdempotence = true,
            Acks = Acks.All,
            // Bounded wait per message when the cluster is unreachable; the row then records a
            // failed attempt instead of holding the batch transaction for librdkafka's 300 s default.
            MessageTimeoutMs = options.ProduceTimeoutMs,
            // Messages are produced one at a time (see the class remarks): batching would only add latency.
            LingerMs = 0,
            // Topics are created from contracts/events/topics.yaml, never by a produce or a metadata request.
            AllowAutoCreateTopics = false,
        };
    }

    public async Task<PublishResult> PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var producer = GetProducer();

        var topicFailure = await PreflightTopicAsync(message.Topic).ConfigureAwait(false);
        if (topicFailure is not null)
        {
            return topicFailure.Value;
        }

        Message<string, byte[]> kafkaMessage;
        try
        {
            kafkaMessage = new Message<string, byte[]>
            {
                Key = message.AggregateId.ToString("D"),
                Value = await _encoder.EncodeAsync(message, cancellationToken).ConfigureAwait(false),
                Headers = RelayKafkaHeaders.Build(message),
            };
        }
        catch (PayloadEncodingException ex)
        {
            return PublishResult.Failure(Describe(ex), ex.IsTransient ? PublishFailureKind.InfraGlobal : PublishFailureKind.Row);
        }
#pragma warning disable CA1031 // Contract of IEventPublisher: failures are returned, never thrown.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            // I/O towards the registry is infrastructure; anything else unexpected while preparing
            // the message (a bug, data the code did not foresee) is about this row: treating it as
            // transient would stop every batch on this row and freeze the whole relay.
            return IsInfrastructureException(ex)
                ? PublishResult.Failure($"Unexpected {ex.GetType().Name} preparing the message: {ex.Message}", PublishFailureKind.InfraGlobal)
                : PublishResult.Failure($"Unexpected {ex.GetType().Name} preparing the message.", PublishFailureKind.Row);
        }

        try
        {
            var report = await producer.ProduceAsync(message.Topic, kafkaMessage, CancellationToken.None).ConfigureAwait(false);
            return report.Status == PersistenceStatus.Persisted
                ? PublishResult.Success
                : PublishResult.Failure($"Kafka delivery status {report.Status}.", PublishFailureKind.InfraGlobal);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            return ProduceFailure(message.Topic, ex.Error);
        }
        catch (KafkaException ex)
        {
            return ProduceFailure(message.Topic, ex.Error);
        }
#pragma warning disable CA1031 // Contract of IEventPublisher: failures are returned, never thrown.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return PublishResult.Failure($"Unexpected {ex.GetType().Name} producing to Kafka: {ex.Message}", PublishFailureKind.InfraGlobal);
        }
    }

    public void Dispose()
    {
        _admin?.Dispose();
        if (_producer is null)
        {
            return;
        }

        try
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _producer.Dispose();
        }
    }

    /// <summary>Classifies a produce error; a topic error also refreshes the preflight cache.</summary>
    internal PublishResult ProduceFailure(string topic, Error error)
    {
        if (error.IsFatal)
        {
            // A fatal error poisons an idempotent producer (e.g. sequence desync): every later
            // produce would fail. Replace it; the row is retried in a later batch.
            RecreateProducer(error);
        }

        var description = $"Kafka error {error.Code} on topic '{topic}': {error.Reason}";
        if (TopicErrors.Contains(error.Code))
        {
            _topics[topic] = (description, _timeProvider.GetUtcNow() + NegativeTopicTtl);
            return PublishResult.Failure(description, PublishFailureKind.TopicUnreachable);
        }

        return PublishResult.Failure(description, RowErrors.Contains(error.Code) ? PublishFailureKind.Row : PublishFailureKind.InfraGlobal);
    }

    /// <summary>
    /// Null when the topic can be produced to; otherwise the failure (TopicUnreachable, or
    /// InfraGlobal when the cluster cannot be asked).
    /// </summary>
    private async Task<PublishResult?> PreflightTopicAsync(string topic)
    {
        var now = _timeProvider.GetUtcNow();
        if (_topics.TryGetValue(topic, out var cached) && now < cached.Until)
        {
            // Negative cache: nothing new was asked of the cluster, so no attempt is recorded.
            return cached.Error is null ? null : PublishResult.Deferred(cached.Error);
        }

        Metadata metadata;
        try
        {
            var admin = _admin!;
            // librdkafka's metadata call is synchronous; keep it off the caller's thread.
            metadata = await Task.Run(() => admin.GetMetadata(topic, _preflightTimeout)).ConfigureAwait(false);
        }
        catch (KafkaException ex)
        {
            return PublishResult.Failure($"Kafka metadata for topic '{topic}' unavailable: {ex.Error.Code}: {ex.Error.Reason}", PublishFailureKind.InfraGlobal);
        }

        var entry = metadata.Topics.FirstOrDefault(t => t.Topic == topic);
        string? error = entry switch
        {
            null => $"Kafka topic '{topic}' does not exist (no metadata).",
            { Error.Code: not ErrorCode.NoError } => $"Kafka topic '{topic}' is not available: {entry.Error.Code}.",
            { Partitions.Count: 0 } => $"Kafka topic '{topic}' has no partitions.",
            _ => null,
        };

        _topics[topic] = (error, now + (error is null ? PositiveTopicTtl : NegativeTopicTtl));
        return error is null ? null : PublishResult.Failure(error, PublishFailureKind.TopicUnreachable);
    }

    private static bool IsInfrastructureException(Exception ex) =>
        ex is IOException or System.Net.Http.HttpRequestException or System.Net.Sockets.SocketException or TimeoutException or TaskCanceledException;

    private static string Describe(Exception ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} <- {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";

    private IProducer<string, byte[]> GetProducer()
    {
        if (_producer is null)
        {
            _producer = BuildProducer();
            _admin = new DependentAdminClientBuilder(_producer.Handle).Build();
        }

        return _producer;
    }

    private IProducer<string, byte[]> BuildProducer() =>
        new ProducerBuilder<string, byte[]>(_config)
            .SetErrorHandler((_, error) => LogKafkaError(_logger, error.Code, error.IsFatal, error.Reason))
            .Build();

    private void RecreateProducer(Error error)
    {
        LogRecreatingProducer(_logger, error.Code);
        var oldProducer = _producer;
        var oldAdmin = _admin;
        _producer = BuildProducer();
        _admin = new DependentAdminClientBuilder(_producer.Handle).Build();
        oldAdmin?.Dispose();
        oldProducer?.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka client error {Code} (fatal={IsFatal}): {Reason}")]
    private static partial void LogKafkaError(ILogger logger, ErrorCode code, bool isFatal, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal Kafka producer error {Code}; recreating the idempotent producer.")]
    private static partial void LogRecreatingProducer(ILogger logger, ErrorCode code);
}

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
/// </summary>
public sealed partial class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private static readonly HashSet<ErrorCode> PermanentErrors =
    [
        // Problems with this message or its topic, not with the cluster: retrying the next row of
        // another aggregate can still succeed, so the batch continues.
        ErrorCode.MsgSizeTooLarge,
        ErrorCode.UnknownTopicOrPart,
        ErrorCode.Local_UnknownTopic,
        ErrorCode.Local_UnknownPartition,
        ErrorCode.TopicAuthorizationFailed,
        ErrorCode.InvalidRecord,
        ErrorCode.TopicException,
        ErrorCode.Local_KeySerialization,
        ErrorCode.Local_ValueSerialization,
    ];

    private readonly IPayloadEncoder _encoder;
    private readonly ProducerConfig _config;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private IProducer<string, byte[]> _producer;

    public KafkaEventPublisher(IPayloadEncoder encoder, IOptions<RelayOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _encoder = encoder;
        _logger = logger;
        _config = CreateProducerConfig(options.Value);
        _producer = BuildProducer();
    }

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
        };
    }

    public async Task<PublishResult> PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

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
            return PublishResult.Failure(Describe(ex), ex.IsTransient);
        }
#pragma warning disable CA1031 // Contract of IEventPublisher: failures are returned, never thrown.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            // An encoder bug or an unexpected client exception: transient, so the batch stops and
            // backs off rather than escaping and being mistaken for a lost leadership.
            return PublishResult.Failure($"Unexpected {ex.GetType().Name} preparing the message: {ex.Message}", isTransient: true);
        }

        try
        {
            var report = await _producer.ProduceAsync(message.Topic, kafkaMessage, CancellationToken.None).ConfigureAwait(false);
            return report.Status == PersistenceStatus.Persisted
                ? PublishResult.Success
                : PublishResult.Failure($"Kafka delivery status {report.Status}.", isTransient: true);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            if (ex.Error.IsFatal)
            {
                // A fatal error poisons an idempotent producer (e.g. sequence desync): every later
                // produce would fail. Replace it; the row is retried in a later batch.
                RecreateProducer(ex.Error);
            }

            return PublishResult.Failure($"Kafka error {ex.Error.Code}: {ex.Error.Reason}", !PermanentErrors.Contains(ex.Error.Code));
        }
        catch (KafkaException ex)
        {
            if (ex.Error.IsFatal)
            {
                RecreateProducer(ex.Error);
            }

            return PublishResult.Failure($"Kafka error {ex.Error.Code}: {ex.Error.Reason}", !PermanentErrors.Contains(ex.Error.Code));
        }
#pragma warning disable CA1031 // Contract of IEventPublisher: failures are returned, never thrown.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return PublishResult.Failure($"Unexpected {ex.GetType().Name} producing to Kafka: {ex.Message}", isTransient: true);
        }
    }

    public void Dispose()
    {
        try
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
        }
        finally
        {
            _producer.Dispose();
        }
    }

    private static string Describe(Exception ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} <- {ex.InnerException.GetType().Name}: {ex.InnerException.Message}";

    private IProducer<string, byte[]> BuildProducer() =>
        new ProducerBuilder<string, byte[]>(_config)
            .SetErrorHandler((_, error) => LogKafkaError(_logger, error.Code, error.IsFatal, error.Reason))
            .Build();

    private void RecreateProducer(Error error)
    {
        LogRecreatingProducer(_logger, error.Code);
        var old = _producer;
        _producer = BuildProducer();
        old.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kafka client error {Code} (fatal={IsFatal}): {Reason}")]
    private static partial void LogKafkaError(ILogger logger, ErrorCode code, bool isFatal, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal Kafka producer error {Code}; recreating the idempotent producer.")]
    private static partial void LogRecreatingProducer(ILogger logger, ErrorCode code);
}

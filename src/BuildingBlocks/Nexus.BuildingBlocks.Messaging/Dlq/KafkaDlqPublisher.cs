using Confluent.Kafka;

namespace Nexus.BuildingBlocks.Messaging.Dlq;

/// <summary>
/// Idempotent Confluent.Kafka producer that forwards raw bytes to <c>{topic}.DLQ</c>. One publish fails
/// after at most <c>message.timeout.ms</c> (<see cref="NexusKafkaOptions.DlqMessageTimeout"/>, default
/// 30 s) instead of librdkafka's 5 min default, so a consumer whose DLQ is unreachable notices it within
/// its poll budget and pauses (see <see cref="NexusConsumer{TEvent}"/>).
/// </summary>
public sealed class KafkaDlqPublisher : IDlqPublisher, IDisposable
{
    /// <summary>Default <c>message.timeout.ms</c> of the DLQ producer.</summary>
    public static readonly TimeSpan DefaultMessageTimeout = TimeSpan.FromSeconds(30);

    private readonly IProducer<string?, byte[]?> _producer;
    private readonly TimeProvider _time;

    public KafkaDlqPublisher(string bootstrapServers, TimeProvider time, TimeSpan? messageTimeout = null)
        : this(new ProducerBuilder<string?, byte[]?>(CreateConfig(bootstrapServers, messageTimeout ?? DefaultMessageTimeout)).Build(), time)
    {
    }

    internal static ProducerConfig CreateConfig(string bootstrapServers, TimeSpan messageTimeout)
    {
        if (messageTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(messageTimeout), messageTimeout, "The DLQ message timeout must be positive.");
        }

        return new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = (int)messageTimeout.TotalMilliseconds,
        };
    }

    internal KafkaDlqPublisher(IProducer<string?, byte[]?> producer, TimeProvider time)
    {
        _producer = producer;
        _time = time;
    }

    public async Task PublishAsync(
        string topic, string? key, byte[]? rawValue, Headers? originalHeaders,
        Exception failure, string consumerGroup, CancellationToken ct = default)
    {
        var message = new Message<string?, byte[]?>
        {
            Key = key,
            Value = rawValue,
            Headers = DlqHeaders.Build(originalHeaders, failure, consumerGroup, _time.GetUtcNow()),
        };
        await _producer.ProduceAsync(DlqHeaders.TopicFor(topic), message, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

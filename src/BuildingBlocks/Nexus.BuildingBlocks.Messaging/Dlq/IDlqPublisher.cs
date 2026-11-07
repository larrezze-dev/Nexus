using Confluent.Kafka;

namespace Nexus.BuildingBlocks.Messaging.Dlq;

public interface IDlqPublisher
{
    /// <summary>
    /// Produces the ORIGINAL record (key, raw value bytes, original headers) to <c>{topic}.DLQ</c> with the
    /// <c>nexus-dlq-*</c> headers added. Completes only when the broker acknowledged (idempotent producer).
    /// </summary>
    Task PublishAsync(
        string topic,
        string? key,
        byte[]? rawValue,
        Headers? originalHeaders,
        Exception failure,
        string consumerGroup,
        CancellationToken ct = default);
}

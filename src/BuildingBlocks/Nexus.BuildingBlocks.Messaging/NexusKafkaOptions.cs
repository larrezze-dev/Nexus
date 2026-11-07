namespace Nexus.BuildingBlocks.Messaging;

/// <summary>
/// Kafka connection of a consuming host, bound from the <c>Nexus:Kafka</c> section (env vars:
/// <c>NEXUS__KAFKA__BOOTSTRAPSERVERS</c>, <c>NEXUS__KAFKA__SCHEMAREGISTRYURL</c>, ...). Defaults are
/// the local compose ports.
/// <para>
/// <b>Time budget of one record</b> (why <see cref="MaxPollInterval"/> is 5 min). Between two polls the
/// consumer loop spends at most: 4 attempts × (handler time, bounded in practice by the Npgsql command
/// timeout, 30 s by default, per statement) + 6.2 s of back-off (200 ms + 1 s + 5 s) + one DLQ publish
/// (≤ <see cref="DlqMessageTimeout"/>, 30 s). With a handler that issues one or two statements that is
/// about 4 × 60 s + 6.2 s + 30 s ≈ 276 s &lt; 300 s. A consumer whose single attempt can take longer must
/// raise <see cref="MaxPollInterval"/>, otherwise the broker evicts it from the group mid-record (the
/// record is then redelivered to the new owner and the inbox keeps the effect single). If the DLQ publish
/// fails, the loop pauses its partitions and keeps polling while it retries, so that wait does not count
/// against <see cref="MaxPollInterval"/>.
/// </para>
/// </summary>
public sealed class NexusKafkaOptions
{
    public const string SectionName = "Nexus:Kafka";

    public string BootstrapServers { get; set; } = "localhost:9092";

    public string SchemaRegistryUrl { get; set; } = "http://localhost:8081";

    /// <summary>
    /// <c>message.timeout.ms</c> of the DLQ producer: how long one DLQ publish may wait for the broker's
    /// acknowledgement (including the idempotent producer's internal retries) before it fails and the
    /// consumer falls back to pausing and retrying. Default 30 s.
    /// </summary>
    public TimeSpan DlqMessageTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary><c>max.poll.interval.ms</c> of every consumer (set explicitly; see the time budget above). Default 5 min.</summary>
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromMinutes(5);
}

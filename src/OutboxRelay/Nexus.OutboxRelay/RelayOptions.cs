namespace Nexus.OutboxRelay;

/// <summary>
/// Options of the outbox relay, bound from the <c>Nexus:Relay</c> section (env vars:
/// <c>NEXUS__RELAY__&lt;KEY&gt;</c>). Defaults are the step 0.7 values.
/// </summary>
public sealed class RelayOptions
{
    public const string SectionName = "Nexus:Relay";

    /// <summary>
    /// Npgsql connection string for role <c>svc_relay</c>. The password is never committed:
    /// it comes from the environment (compose: <c>NEXUS__RELAY__CONNECTIONSTRING</c>) or
    /// user-secrets.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string KafkaBootstrapServers { get; set; } = "localhost:9092";

    public string SchemaRegistryUrl { get; set; } = "http://localhost:8081";

    public int BatchSize { get; set; } = 500;

    public int PollIntervalMs { get; set; } = 500;

    public int LeaderRetrySeconds { get; set; } = 5;

    public int MaxAttempts { get; set; } = 10;

    public int PurgeRetentionDays { get; set; } = 7;

    /// <summary>
    /// [+] Kafka <c>message.timeout.ms</c>: how long one produce may wait for the cluster before
    /// the row records a failed attempt (librdkafka's default, 300 s, would hold the batch).
    /// </summary>
    public int ProduceTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// [+] Cap of the exponential back-off after a transient failure (Kafka or Schema Registry
    /// unavailable), starting at <see cref="PollIntervalMs"/>. With the defaults, the 10 attempts
    /// of the head row span about 4 minutes of outage before it becomes FAILED.
    /// </summary>
    public int MaxBackoffSeconds { get; set; } = 60;

    /// <summary>[+] Minimum hours between two purges of PUBLISHED rows (conventions-events.md §4.6: daily).</summary>
    public int PurgeIntervalHours { get; set; } = 24;

    /// <summary>[+] Rows deleted per purge statement, so locks stay short.</summary>
    public int PurgeChunkSize { get; set; } = 5000;
}

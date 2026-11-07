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

    /// <summary>
    /// A row becomes FAILED when a <see cref="PublishFailureKind.Row"/> failure (payload or headers
    /// invalid, local <c>.avsc</c> missing, content not registered, message too large) leaves it with
    /// <c>attempts &gt;= MaxAttempts</c>. <see cref="PublishFailureKind.InfraGlobal"/> and
    /// <see cref="PublishFailureKind.TopicUnreachable"/> failures increment <c>attempts</c> too but
    /// never make it FAILED.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    public int PurgeRetentionDays { get; set; } = 7;

    /// <summary>
    /// [+] Kafka <c>message.timeout.ms</c>: how long one produce may wait for the cluster before
    /// the row records a failed attempt (librdkafka's default, 300 s, would hold the batch).
    /// </summary>
    public int ProduceTimeoutMs { get; set; } = 30_000;

    /// <summary>
    /// [+] Bound of the topic metadata preflight done before producing (see
    /// <c>KafkaEventPublisher</c>): a missing or unauthorised topic is detected in this time instead
    /// of after <see cref="ProduceTimeoutMs"/>.
    /// </summary>
    public int TopicPreflightTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// [+] Cap of the exponential back-off after an InfraGlobal failure (Kafka or Schema Registry
    /// unavailable), starting at <see cref="PollIntervalMs"/>. During an outage the head row's
    /// <c>attempts</c> keep growing (one per back-off step, at most one per minute with the
    /// defaults) but it never becomes FAILED, however long the outage lasts (conventions-events.md
    /// §4 step 5): only Row failures count towards <see cref="MaxAttempts"/>.
    /// </summary>
    public int MaxBackoffSeconds { get; set; } = 60;

    /// <summary>[+] Minimum hours between two purges of PUBLISHED rows (conventions-events.md §4.6: daily).</summary>
    public int PurgeIntervalHours { get; set; } = 24;

    /// <summary>[+] Rows deleted per purge statement, so locks stay short.</summary>
    public int PurgeChunkSize { get; set; } = 5000;

    /// <summary>
    /// [+] Maximum purge statements per pass of the leader loop. A larger backlog is resumed on
    /// the next pass (after the next batch), so the purge never holds publication for long.
    /// </summary>
    public int PurgeMaxChunksPerPass { get; set; } = 10;

    /// <summary>
    /// [+] Maximum aggregates held back in memory because their topic is unreachable (excluded from
    /// the batch query until the topic answers). Beyond it, rows of those topics are selected and
    /// skipped on every pass and can starve later rows again (alert on
    /// <c>nexus_outbox_oldest_pending_age_seconds</c>; see <c>nexus_outbox_held_aggregates</c>).
    /// </summary>
    public int MaxHeldAggregates { get; set; } = HeldAggregates.DefaultCapacity;

    /// <summary>
    /// [+] Root of the Avro contracts, laid out like <c>contracts/events</c> (ADR-0018): the relay
    /// encodes each row with the local <c>&lt;root&gt;/&lt;context&gt;/&lt;aggregate&gt;.&lt;event&gt;.v&lt;n&gt;.avsc</c>
    /// its writer validated. Empty: <c>contracts/events</c> next to the binaries (copied there by the
    /// build). The container sets it explicitly (<c>NEXUS__RELAY__CONTRACTSDIRECTORY</c>).
    /// </summary>
    public string ContractsDirectory { get; set; } = string.Empty;

    /// <summary>Resolved <see cref="ContractsDirectory"/>.</summary>
    public string ResolveContractsDirectory() =>
        string.IsNullOrWhiteSpace(ContractsDirectory) ? Path.Combine(AppContext.BaseDirectory, "contracts", "events") : ContractsDirectory;
}

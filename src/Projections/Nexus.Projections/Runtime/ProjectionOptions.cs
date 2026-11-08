namespace Nexus.Projections.Runtime;

/// <summary>
/// Settings of the projection runtime, bound from <c>Nexus:Projections</c> (env vars:
/// <c>NEXUS__PROJECTIONS__MAXBATCHROWS</c>, ...). Defaults follow the step 0.8 ficha (batches of at most
/// 1 000 rows or 1 s).
/// </summary>
public sealed class ProjectionOptions
{
    public const string SectionName = "Nexus:Projections";

    /// <summary>A batch is flushed as soon as it holds this many rows. Default 1 000.</summary>
    public int MaxBatchRows { get; set; } = 1000;

    /// <summary>A batch is flushed at the latest this long after its first record was buffered. Default 1 s.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Directory with the ClickHouse migrations (<c>V*.sql</c>). Empty = <c>&lt;AppContext.BaseDirectory&gt;/db/clickhouse</c>,
    /// where the build copies <c>db/clickhouse/V*.sql</c>.
    /// </summary>
    public string MigrationsDirectory { get; set; } = string.Empty;

    /// <summary>
    /// <c>topic.metadata.refresh.interval.ms</c> of every consumer: how soon a regex subscription picks up a
    /// newly created topic. Default 30 s.
    /// </summary>
    public TimeSpan TopicMetadataRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>First wait after a transient infrastructure failure (ClickHouse, Schema Registry); doubled after each further failure.</summary>
    public TimeSpan TransientInitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Cap of the transient-failure back-off. Default 30 s.</summary>
    public TimeSpan TransientMaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <c>schema.registry.request.timeout.ms</c>: how long one Schema Registry lookup may take before it
    /// counts as a transient failure. Default 10 s.
    /// </summary>
    public TimeSpan SchemaRegistryRequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Timeout of the Kafka admin requests of <c>rebuild</c> (describe/delete group, metadata, offsets). Default 15 s.</summary>
    public TimeSpan AdminRequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    internal string ResolveMigrationsDirectory() =>
        string.IsNullOrWhiteSpace(MigrationsDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "db", "clickhouse")
            : MigrationsDirectory;

    internal void Validate()
    {
        if (MaxBatchRows < 1)
            throw new InvalidOperationException($"{SectionName}:MaxBatchRows must be at least 1.");
        if (FlushInterval <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:FlushInterval must be positive.");
        if (TopicMetadataRefreshInterval <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}:TopicMetadataRefreshInterval must be positive.");
        if (TransientInitialBackoff <= TimeSpan.Zero || TransientMaxBackoff < TransientInitialBackoff)
            throw new InvalidOperationException($"{SectionName}:TransientInitialBackoff must be positive and not above TransientMaxBackoff.");
        if (SchemaRegistryRequestTimeout <= TimeSpan.Zero || AdminRequestTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException($"{SectionName}: request timeouts must be positive.");
    }
}

/// <summary>ClickHouse connection, bound from <c>Nexus:ClickHouse</c> (<c>NEXUS__CLICKHOUSE__CONNECTIONSTRING</c>).</summary>
public sealed class ClickHouseOptions
{
    public const string SectionName = "Nexus:ClickHouse";

    /// <summary>
    /// ClickHouse.Client connection string (HTTP), e.g. <c>Host=localhost;Port=8123;Username=nexus;Password=...</c>.
    /// The password never lives in the repo: set it through the environment or user secrets. The
    /// <c>Database</c> part is ignored by the migration applier, which always connects to <c>default</c>
    /// (<c>db/clickhouse/README.md</c>); every statement uses qualified names.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}

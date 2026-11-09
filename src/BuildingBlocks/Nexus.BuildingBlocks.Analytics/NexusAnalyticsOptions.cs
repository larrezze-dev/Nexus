namespace Nexus.BuildingBlocks.Analytics;

/// <summary>
/// ClickHouse settings of a service that runs analytic queries, read by
/// <see cref="AnalyticsServiceCollectionExtensions.AddNexusAnalytics"/> from
/// <see cref="SectionName"/> (<c>Nexus:ClickHouse</c>):
/// <code>
/// "Nexus": {
///   "ClickHouse": {
///     "ConnectionString": "Host=localhost;Port=8123;Database=nexus_analytics;Username=nexus",
///     "Password": "...",           // never in appsettings.json: user-secrets / env var / Key Vault
///     "QueryTimeout": "00:00:30"   // optional; also accepts plain seconds ("30")
///   }
/// }
/// </code>
/// Environment variables: <c>Nexus__ClickHouse__ConnectionString</c>,
/// <c>Nexus__ClickHouse__Password</c>, <c>Nexus__ClickHouse__QueryTimeout</c>. All are read on the first analytic query, never at
/// startup: a host with no ClickHouse configured (or running) starts and serves everything else.
/// </summary>
public sealed class NexusAnalyticsOptions
{
    /// <summary>Configuration section the options are read from.</summary>
    public const string SectionName = "Nexus:ClickHouse";

    /// <summary>ClickHouse.Client connection string (HTTP interface). Required on first query.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Optional password applied on top of <see cref="ConnectionString"/> (wins when set).</summary>
    public string? Password { get; set; }

    /// <summary>Default for <see cref="QueryTimeout"/>.</summary>
    public static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Upper bound of one analytic statement. Enforced twice: by ClickHouse itself
    /// (<c>max_execution_time</c>, whole seconds, rounded up) and, as a backstop a few seconds
    /// later, by the HTTP client. Either way the caller gets a <see cref="TimeoutException"/>.
    /// Must be positive. Default 30 s.
    /// </summary>
    public TimeSpan QueryTimeout { get; set; } = DefaultQueryTimeout;
}

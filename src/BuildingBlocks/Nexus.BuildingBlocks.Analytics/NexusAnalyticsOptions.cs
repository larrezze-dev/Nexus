namespace Nexus.BuildingBlocks.Analytics;

/// <summary>
/// ClickHouse settings of a service that runs analytic queries, read by
/// <see cref="AnalyticsServiceCollectionExtensions.AddNexusAnalytics"/> from
/// <see cref="SectionName"/> (<c>Nexus:ClickHouse</c>):
/// <code>
/// "Nexus": {
///   "ClickHouse": {
///     "ConnectionString": "Host=localhost;Port=8123;Database=nexus_analytics;Username=nexus",
///     "Password": "..."            // never in appsettings.json: user-secrets / env var / Key Vault
///   }
/// }
/// </code>
/// Environment variables: <c>Nexus__ClickHouse__ConnectionString</c>,
/// <c>Nexus__ClickHouse__Password</c>. Both are read on the first analytic query, never at
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
}

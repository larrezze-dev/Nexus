using ClickHouse.Client.ADO;
using Microsoft.Extensions.Options;

namespace Nexus.Projections.Runtime;

/// <summary>
/// Creates ClickHouse connections from <see cref="ClickHouseOptions.ConnectionString"/>, always on the
/// <c>default</c> database: the migration applier needs it (<c>nexus_analytics</c> may not exist yet,
/// db/clickhouse/README.md) and every projection uses fully qualified table names anyway.
/// </summary>
public sealed class ClickHouseConnectionProvider
{
    private readonly string _connectionString;

    public ClickHouseConnectionProvider(IOptions<ClickHouseOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ClickHouseOptions.SectionName}:ConnectionString is not set (env NEXUS__CLICKHOUSE__CONNECTIONSTRING).");
        }

        _connectionString = new ClickHouseConnectionStringBuilder(configured) { Database = "default" }.ConnectionString;
    }

    /// <summary>A new, unopened connection (HTTP: cheap; the client pools the underlying handler).</summary>
    public ClickHouseConnection Create() => new(_connectionString);
}

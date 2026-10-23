using Npgsql;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// The single <see cref="NpgsqlDataSource"/> of a service (conventions-backend.md §4, step 1).
/// One instance per service process, registered as a singleton by the host. The connection
/// string is supplied by the host configuration and must authenticate as that service's own
/// role (<c>svc_&lt;service&gt;</c>, conventions-database.md §2); this type never knows or
/// hardcodes which role that is.
///
/// Business code never opens connections from here directly: it goes through
/// <see cref="IUnitOfWork"/>, which opens the connection, starts the transaction and stamps the
/// RLS session context before any query runs (BE-R1).
/// </summary>
public sealed class ServiceDataSource : IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Builds the data source from the service's connection string.</summary>
    public ServiceDataSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <summary>
    /// Wraps an already built data source (e.g. one created with
    /// <see cref="NpgsqlDataSourceBuilder"/> to add OpenTelemetry tracing). Ownership is
    /// transferred: disposing this instance disposes <paramref name="dataSource"/>.
    /// </summary>
    public ServiceDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <summary>Opens a new pooled connection authenticated as the service role.</summary>
    public ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        _dataSource.OpenConnectionAsync(cancellationToken);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    public void Dispose() => _dataSource.Dispose();
}

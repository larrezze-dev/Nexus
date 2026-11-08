using System.Net;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Options;

namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <summary>Creates (unopened) ClickHouse connections. Internal seam so tests can prove that an
/// empty scope never reaches ClickHouse.</summary>
internal interface IAnalyticsConnectionFactory
{
    ClickHouseConnection Create();
}

/// <summary>
/// Singleton. Everything is lazy: the options are read and the shared <see cref="HttpClient"/> is
/// built on the first <see cref="Create"/>, i.e. on the first analytic query, so a host starts
/// (and serves <c>/health</c>, <c>/openapi</c>) with no ClickHouse configured or running.
/// <para>
/// One <see cref="HttpClient"/> for the process, passed to every connection (the client does not
/// dispose a supplied one): a new handler per query would exhaust sockets.
/// <c>PooledConnectionLifetime</c> keeps DNS changes visible; automatic decompression matches
/// the client's own default handler (it requests compressed responses).
/// </para>
/// </summary>
internal sealed class AnalyticsConnectionFactory : IAnalyticsConnectionFactory, IDisposable
{
    private readonly IOptions<NexusAnalyticsOptions> _options;
    private readonly Lazy<(string ConnectionString, HttpClient HttpClient)> _state;

    public AnalyticsConnectionFactory(IOptions<NexusAnalyticsOptions> options)
    {
        _options = options;
        _state = new Lazy<(string, HttpClient)>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ClickHouseConnection Create()
    {
        var (connectionString, httpClient) = _state.Value;
        return new ClickHouseConnection(connectionString, httpClient);
    }

    public void Dispose()
    {
        if (_state.IsValueCreated)
        {
            _state.Value.HttpClient.Dispose();
        }
    }

    private (string, HttpClient) Build()
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Missing configuration '{NexusAnalyticsOptions.SectionName}:ConnectionString' " +
                "(ClickHouse connection string for analytic queries, conventions-api.md §7).");
        }

        var builder = new ClickHouseConnectionStringBuilder(options.ConnectionString);
        if (!string.IsNullOrEmpty(options.Password))
        {
            builder.Password = options.Password;
        }

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        return (builder.ConnectionString, new HttpClient(handler, disposeHandler: true));
    }
}

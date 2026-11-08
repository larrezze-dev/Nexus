using System.Net;
using ClickHouse.Client.ADO;
using Microsoft.Extensions.Options;

namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <summary>Creates (unopened) ClickHouse connections. Internal seam so tests can prove that an
/// empty scope never reaches ClickHouse.</summary>
internal interface IAnalyticsConnectionFactory
{
    /// <summary>The bound of one statement (<see cref="NexusAnalyticsOptions.QueryTimeout"/>).</summary>
    TimeSpan QueryTimeout { get; }

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
/// <para>
/// Bounded and cancellable queries. Every connection carries, as per-query ClickHouse settings:
/// <list type="bullet">
///   <item><c>max_execution_time</c> = <see cref="NexusAnalyticsOptions.QueryTimeout"/> (whole
///   seconds, rounded up): ClickHouse aborts the statement itself.</item>
///   <item><c>cancel_http_readonly_queries_on_client_close = 1</c>: when the HTTP request is
///   aborted (the caller's <see cref="CancellationToken"/>, e.g. the browser went away, or the
///   client-side timeout) ClickHouse stops the query instead of finishing it for nobody.</item>
///   <item><c>readonly = 2</c>: required by the previous setting, which ClickHouse only honours
///   for read-only queries (a GET is read-only implicitly; this client POSTs). Level 2 still
///   allows per-query settings such as the two above, and doubles as defence in depth: the
///   analytic path cannot write or run DDL. The ClickHouse user must not be pinned to
///   <c>readonly = 1</c> in its profile (that level forbids changing settings).</item>
/// </list>
/// The <see cref="HttpClient.Timeout"/> is the query timeout plus <see cref="HttpTimeoutMargin"/>:
/// a backstop that should normally never fire, so the server's own, more precise error wins.
/// </para>
/// </summary>
internal sealed class AnalyticsConnectionFactory : IAnalyticsConnectionFactory, IDisposable
{
    /// <summary>How much later than the server-side limit the HTTP client gives up.</summary>
    internal static readonly TimeSpan HttpTimeoutMargin = TimeSpan.FromSeconds(5);

    private readonly IOptions<NexusAnalyticsOptions> _options;
    private readonly Lazy<State> _state;

    public AnalyticsConnectionFactory(IOptions<NexusAnalyticsOptions> options)
    {
        _options = options;
        _state = new Lazy<State>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public TimeSpan QueryTimeout => _state.Value.QueryTimeout;

    public ClickHouseConnection Create()
    {
        var state = _state.Value;
        var connection = new ClickHouseConnection(state.ConnectionString, state.HttpClient);
        connection.CustomSettings["max_execution_time"] = state.MaxExecutionSeconds;
        connection.CustomSettings["cancel_http_readonly_queries_on_client_close"] = 1;
        connection.CustomSettings["readonly"] = 2;
        return connection;
    }

    public void Dispose()
    {
        if (_state.IsValueCreated)
        {
            _state.Value.HttpClient.Dispose();
        }
    }

    private State Build()
    {
        var options = _options.Value;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"Missing configuration '{NexusAnalyticsOptions.SectionName}:ConnectionString' " +
                "(ClickHouse connection string for analytic queries, conventions-api.md §7).");
        }

        if (options.QueryTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"'{NexusAnalyticsOptions.SectionName}:{nameof(NexusAnalyticsOptions.QueryTimeout)}' must be positive (was {options.QueryTimeout}).");
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

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = options.QueryTimeout + HttpTimeoutMargin,
        };

        return new State(
            builder.ConnectionString,
            httpClient,
            options.QueryTimeout,
            (int)Math.Ceiling(options.QueryTimeout.TotalSeconds));
    }

    private sealed record State(string ConnectionString, HttpClient HttpClient, TimeSpan QueryTimeout, int MaxExecutionSeconds);
}

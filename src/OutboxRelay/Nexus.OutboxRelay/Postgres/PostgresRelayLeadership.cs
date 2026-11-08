using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Nexus.OutboxRelay.Postgres;

/// <summary>
/// Leader election with a session-level advisory lock, <c>pg_try_advisory_lock(hashtext('nexus-relay'))</c>
/// (conventions-events.md §4.1, ficha 0.7 B.1).
///
/// <para>
/// The lock lives on a <b>dedicated, non-pooled</b> connection (<c>Pooling=false</c>): a pooled
/// connection would be "returned" on dispose while its physical session — and the lock — stayed
/// alive in the pool, and could later be handed to unrelated code. With pooling off, closing the
/// connection ends the session and PostgreSQL releases the lock.
/// </para>
///
/// <para>
/// That same connection is also the one that runs every batch transaction and the
/// <c>LISTEN outbox_new</c>. This is deliberate fencing: rows can only be locked and marked by the
/// session that owns the leader lock, so if that session dies (network cut, backend terminated,
/// server restart) the open batch transaction dies with it, its row locks go, and its marks are
/// rolled back — at the very moment another instance can obtain the lock. Using a second
/// connection for the batches would open a window in which a former leader still holds row locks
/// and keeps publishing after the new leader has started, which would let the new leader skip
/// (SKIP LOCKED) the former leader's earlier rows of an aggregate and publish later ones first
/// (EV-R3).
/// </para>
///
/// <para>
/// <b>Late in-flight message after a failover (H10).</b> Fencing stops a former leader from
/// <i>marking</i> rows, not a message it had already handed to librdkafka: if its session dies while
/// it awaits the delivery report of row n, the new leader republishes n (and then n+1…), and the
/// former leader's copy of n may still reach the broker afterwards, i.e. after n+1. The topic can
/// therefore contain a late duplicate of an older version. Consumers absorb it by design
/// (conventions-events.md §7): the inbox (<c>inbox.processed_message</c>, keyed by
/// <c>nexus-event-id</c>) drops the duplicate event id, and <c>IVersionedReplica</c> discards any
/// version older than the one already applied. What can never happen is a gap: n+1 is only
/// produced after n was acknowledged by the producing leader.
/// </para>
/// </summary>
public sealed partial class PostgresRelayLeadership : IRelayLeadership
{
    /// <summary>Lock key name, hashed by PostgreSQL's <c>hashtext</c> (int4, promoted to the bigint key).</summary>
    public const string LockName = "nexus-relay";

    /// <summary>
    /// Session GUCs (all <c>USERSET</c>) set at connection start-up through the <c>options</c>
    /// start-up parameter, so the server detects a vanished leader promptly and frees the lock:
    /// <list type="bullet">
    ///   <item><c>tcp_keepalives_idle=5</c>, <c>tcp_keepalives_interval=2</c>,
    ///   <c>tcp_keepalives_count=3</c>: an idle leader socket is declared dead after
    ///   5 + 3 × 2 = 11 s without an answer.</item>
    ///   <item><c>tcp_user_timeout=11000</c> (ms): the same bound when the socket is not idle — e.g. a
    ///   <c>NOTIFY outbox_new</c> sent to the vanished leader stays unacknowledged, which suspends
    ///   keepalives and would otherwise leave the session to the kernel's retransmission timeout
    ///   (about 15 minutes with Linux defaults).</item>
    /// </list>
    /// Worst-case takeover after the leader's node vanishes: about 11 s for the server to drop the
    /// session (and the lock) plus up to <see cref="RelayOptions.LeaderRetrySeconds"/> (5 s) for a
    /// passive instance's next attempt, i.e. about 15–16 s. A leader that crashes with its node
    /// still up (process killed, session terminated) is released at once (the kernel closes the
    /// socket), so the takeover is bounded by <see cref="RelayOptions.LeaderRetrySeconds"/> alone.
    /// </summary>
    internal const string ServerKeepAliveOptions =
        "-c tcp_keepalives_idle=5 -c tcp_keepalives_interval=2 -c tcp_keepalives_count=3 -c tcp_user_timeout=11000";

    /// <summary>Npgsql <c>Keepalive</c> (seconds idle before a client keepalive query).</summary>
    internal const int ClientKeepAliveSeconds = 5;

    private readonly string _connectionString;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PostgresRelayLeadership> _logger;

    public PostgresRelayLeadership(IOptions<RelayOptions> options, ILoggerFactory loggerFactory)
        : this(options.Value.ConnectionString, loggerFactory)
    {
    }

    public PostgresRelayLeadership(string connectionString, ILoggerFactory loggerFactory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = LockName,
            // Client side: detect a silently dead server (half-open TCP) at the OS level, and send
            // a protocol keepalive while idle (also during WaitAsync on LISTEN). The batch queries
            // and the per-batch lock confirmation detect it at the protocol level too.
            TcpKeepAlive = true,
            KeepAlive = ClientKeepAliveSeconds,
        };
        // Server side (H2): the advisory lock is only released when PostgreSQL notices the leader's
        // session is gone. If the leader's node vanishes (power loss, network partition) no FIN/RST
        // ever arrives, and with the OS defaults the backend would hold the lock for hours.
        builder.Options = string.IsNullOrWhiteSpace(builder.Options) ? ServerKeepAliveOptions : $"{builder.Options} {ServerKeepAliveOptions}";
        _connectionString = builder.ConnectionString;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PostgresRelayLeadership>();
    }

    public async Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var command = new NpgsqlCommand("SELECT pg_catalog.pg_try_advisory_lock(pg_catalog.hashtext($1))", connection))
            {
                command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = LockName });
                var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
                if (!acquired)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    return null;
                }
            }

            var lease = new PostgresRelayLease(connection, _loggerFactory.CreateLogger<PostgresRelayLease>());
            try
            {
                await lease.StartListeningAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            LogAcquired(_logger, connection.ProcessID);
            return lease;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Relay leadership acquired (backend pid {BackendPid}).")]
    private static partial void LogAcquired(ILogger logger, int backendPid);
}

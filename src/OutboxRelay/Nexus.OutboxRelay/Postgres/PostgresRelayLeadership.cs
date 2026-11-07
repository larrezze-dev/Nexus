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
/// </summary>
public sealed partial class PostgresRelayLeadership : IRelayLeadership
{
    /// <summary>Lock key name, hashed by PostgreSQL's <c>hashtext</c> (int4, promoted to the bigint key).</summary>
    public const string LockName = "nexus-relay";

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
        _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            ApplicationName = LockName,
            // Detect a silently dead peer (half-open TCP) at the OS level as well; the batch
            // queries and the per-batch lock confirmation detect it at the protocol level.
            TcpKeepAlive = true,
        }.ConnectionString;
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

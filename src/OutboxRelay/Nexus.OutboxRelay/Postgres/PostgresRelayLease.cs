using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Nexus.OutboxRelay.Postgres;

/// <summary>
/// The leader's session: owns the advisory lock, listens on <c>outbox_new</c> and runs every batch
/// transaction (see <see cref="PostgresRelayLeadership"/> for why it is one connection). Not
/// thread-safe: used by the single relay loop only.
/// </summary>
public sealed partial class PostgresRelayLease : IRelayLease
{
    /// <summary>
    /// Confirms, inside the batch transaction and before any row is locked, that this session
    /// still holds the leader lock. A bigint advisory key appears in <c>pg_locks</c> split into
    /// <c>classid</c> (high 32 bits) and <c>objid</c> (low 32 bits) with <c>objsubid = 1</c>.
    /// Should never be false while the session is alive (nothing in the relay unlocks it), but it
    /// is the explicit "never publish without a confirmed lock" check, and it doubles as the
    /// keepalive of the session: if the connection is gone this statement fails.
    /// </summary>
    internal const string ConfirmLockSql =
        """
        SELECT EXISTS (
          SELECT 1
          FROM pg_catalog.pg_locks l,
               (SELECT pg_catalog.hashtext($1)::bigint AS k) key
          WHERE l.locktype = 'advisory'
            AND l.pid = pg_catalog.pg_backend_pid()
            AND l.granted
            AND l.database = (SELECT d.oid FROM pg_catalog.pg_database d WHERE d.datname = pg_catalog.current_database())
            AND l.objsubid = 1
            AND l.classid = ((key.k >> 32) & 4294967295)::oid
            AND l.objid = (key.k & 4294967295)::oid)
        """;

    /// <summary>
    /// The batch query (conventions-events.md §4.3) plus the per-aggregate ordering guard (EV-R3):
    /// a PENDING row is not publishable while an earlier row (by <c>created_at, id</c>) of the
    /// same aggregate is <c>FAILED</c>. Without the guard, once a message exhausts its attempts the
    /// next versions of that aggregate would be published over the gap and consumers would apply
    /// version n+1 without n (a replica that discards older versions could never recover n). The
    /// aggregate is held back instead: it stays blocked, visible through
    /// <c>nexus_outbox_failed_total</c>, until an operator fixes the cause and resets the FAILED row
    /// to PENDING (or decides otherwise); then publication resumes in order.
    ///
    /// The aggregate is identified by <c>aggregate_id</c> alone: it is the Kafka key (§2) and a
    /// UUIDv7, unique across aggregate types.
    ///
    /// Cost: the anti-join is served by the partial index <c>ix_outbox_failed_aggregate</c>
    /// (<c>(aggregate_id, created_at, id) WHERE status = 'FAILED'</c>, migration
    /// <c>V202611071600__infra__outbox_failed_index.sql</c>), so it never scans the PUBLISHED rows.
    /// </summary>
    internal const string SelectBatchSql =
        """
        SELECT m.id, m.topic, m.aggregate_type, m.aggregate_id, m.aggregate_version, m.event_type,
               m.schema_version, m.headers::text, m.payload, m.created_at, m.attempts
        FROM outbox.message m
        WHERE m.status = 'PENDING'
          AND NOT EXISTS (
                SELECT 1
                FROM outbox.message f
                WHERE f.status = 'FAILED'
                  AND f.aggregate_id = m.aggregate_id
                  AND (f.created_at, f.id) < (m.created_at, m.id))
        ORDER BY m.created_at, m.id
        LIMIT $1
        FOR UPDATE OF m SKIP LOCKED
        """;

    private readonly NpgsqlConnection _connection;
    private readonly ILogger<PostgresRelayLease> _logger;
    private bool _notified;
    private bool _disposed;

    internal PostgresRelayLease(NpgsqlConnection connection, ILogger<PostgresRelayLease> logger)
    {
        _connection = connection;
        _logger = logger;
        // Notifications can be read while a batch query is running, not only inside WaitAsync;
        // remember them so the next wait returns immediately instead of losing the wake-up.
        _connection.Notification += (_, _) => _notified = true;
    }

    /// <summary>Backend pid of the leader session (diagnostics and tests).</summary>
    public int BackendProcessId => _connection.ProcessID;

    /// <summary>The leader session itself (tests only: inspect its settings, drop its lock).</summary>
    internal NpgsqlConnection SessionForTests => _connection;

    internal async Task StartListeningAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("LISTEN outbox_new", _connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> WaitForWakeUpAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_notified)
        {
            _notified = false;
            return true;
        }

        try
        {
            var woken = await _connection.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            _notified = false;
            return woken;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            throw new RelayLeadershipLostException("The leader session failed while waiting for outbox notifications.", ex);
        }
    }

    public async Task<IOutboxBatch> BeginBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        NpgsqlTransaction transaction;
        try
        {
            transaction = await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            throw new RelayLeadershipLostException("The leader session is gone.", ex);
        }

        try
        {
            await ConfirmLockAsync(_connection, transaction, cancellationToken).ConfigureAwait(false);

            var messages = new List<OutboxMessage>();
            await using (var command = new NpgsqlCommand(SelectBatchSql, _connection, transaction))
            {
                command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = batchSize, NpgsqlDbType = NpgsqlDbType.Integer });
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    messages.Add(new OutboxMessage(
                        Id: reader.GetGuid(0),
                        Topic: reader.GetString(1),
                        AggregateType: reader.GetString(2),
                        AggregateId: reader.GetGuid(3),
                        AggregateVersion: reader.GetInt64(4),
                        EventType: reader.GetString(5),
                        SchemaVersion: reader.GetInt32(6),
                        HeadersJson: reader.GetString(7),
                        Payload: reader.GetString(8),
                        CreatedAt: reader.GetFieldValue<DateTimeOffset>(9),
                        Attempts: reader.GetInt32(10)));
                }
            }

            return new PostgresOutboxBatch(_connection, transaction, messages);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(transaction).ConfigureAwait(false);
            if (ex is RelayLeadershipLostException)
            {
                throw;
            }

            if (ex is NpgsqlException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
            {
                throw new RelayLeadershipLostException("The leader session failed while locking a batch.", ex);
            }

            throw;
        }
    }

    internal const string PurgeSql =
        """
        DELETE FROM outbox.message
        WHERE id IN (
          SELECT id FROM outbox.message
          WHERE status = 'PUBLISHED' AND published_at < $1
          LIMIT $2)
        """;

    public async Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRows);
        await using var command = new NpgsqlCommand(PurgeSql, _connection);
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = cutoff, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = maxRows, NpgsqlDbType = NpgsqlDbType.Integer });
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await using var command = new NpgsqlCommand("SELECT count(*), min(created_at) FROM outbox.message WHERE status = 'PENDING'", _connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new PendingStats(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // Explicit release for a clean hand-over; closing the non-pooled session releases it anyway.
            if (_connection.FullState == System.Data.ConnectionState.Open)
            {
                await using var command = new NpgsqlCommand("SELECT pg_catalog.pg_advisory_unlock(pg_catalog.hashtext($1))", _connection);
                command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = PostgresRelayLeadership.LockName });
                await command.ExecuteScalarAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Release is best effort: the session end releases the lock regardless.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReleaseFailed(_logger, ex.GetType().Name);
        }
        finally
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Runs <see cref="ConfirmLockSql"/>; throws <see cref="RelayLeadershipLostException"/> unless the lock is held.</summary>
    internal static async Task ConfirmLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        bool held;
        try
        {
            await using var command = new NpgsqlCommand(ConfirmLockSql, connection, transaction);
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = PostgresRelayLeadership.LockName });
            held = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            throw new RelayLeadershipLostException("The leader session failed confirming the lock.", ex);
        }

        if (!held)
        {
            throw new RelayLeadershipLostException("The leader advisory lock is no longer held by this session.");
        }
    }

    private static async Task SafeRollbackAsync(NpgsqlTransaction transaction)
    {
        try
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The session may already be broken; the original error is what matters.
        catch
#pragma warning restore CA1031
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Explicit release of the relay leader lock failed ({ExceptionType}); closing the session releases it.")]
    private static partial void LogReleaseFailed(ILogger logger, string exceptionType);
}

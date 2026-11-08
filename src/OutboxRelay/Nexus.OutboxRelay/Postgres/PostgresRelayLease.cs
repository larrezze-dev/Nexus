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
    /// The batch query (conventions-events.md §4.3) plus two per-aggregate ordering guards (EV-R3).
    ///
    /// <para>
    /// <b>FAILED guard.</b> A PENDING row is not publishable while an earlier row (by
    /// <c>created_at, id</c>) of the same aggregate is <c>FAILED</c>. Without it, once a message
    /// exhausts its attempts the next versions of that aggregate would be published over the gap and
    /// consumers would apply version n+1 without n (a replica that discards older versions could never
    /// recover n). The aggregate is held back instead: it stays blocked, visible through
    /// <c>nexus_outbox_failed_total</c>, until an operator fixes the cause and resets the FAILED row
    /// to PENDING (or decides otherwise); then publication resumes in order.
    /// </para>
    ///
    /// <para>
    /// <b>Unreachable topics</b> (<c>$2</c>, <c>text[]</c>: the topics held in memory because they are
    /// missing or not authorised, <see cref="HeldTopics.Current"/>). Excluded are (a) the PENDING rows
    /// on those topics and (b) the PENDING rows of an aggregate that has an <i>earlier</i> PENDING row
    /// on one of them. So an aggregate spread over several topics waits entirely behind its first row
    /// on an unreachable topic, while its earlier rows on healthy topics still publish. Without the
    /// exclusion, the rows of an unreachable topic at the head of the backlog would fill every batch
    /// and starve the other topics; it is per topic, so it needs no per-aggregate list and has no cap,
    /// however many aggregates the unreachable topic spans.
    /// </para>
    ///
    /// <para>
    /// <c>$2</c> must be a non-null <c>text[]</c> without NULL elements (<c>'{}'</c> when nothing is
    /// held): <c>NOT (topic = ANY(NULL))</c> is NULL, the batch would come back empty and the relay
    /// would stall in silence. <see cref="BeginBatchAsync(int, IReadOnlyCollection{string}, CancellationToken)"/>
    /// enforces it.
    /// </para>
    ///
    /// <para>
    /// The aggregate is identified by <c>aggregate_id</c> alone: it is the Kafka key (§2) and a
    /// UUIDv7, unique across aggregate types.
    /// </para>
    ///
    /// <para>
    /// Cost: the FAILED anti-join is served by the partial index <c>ix_outbox_failed_aggregate</c>
    /// (<c>(aggregate_id, created_at, id) WHERE status = 'FAILED'</c>, migration
    /// <c>V202611071600__infra__outbox_failed_index.sql</c>), so it never scans the PUBLISHED rows.
    /// The PENDING anti-join is served by <c>ix_outbox_pending_aggregate</c>
    /// (<c>(aggregate_id, topic, created_at, id) WHERE status = 'PENDING'</c>, migration
    /// <c>V202611081000__infra__outbox_pending_aggregate_index.sql</c>): one index probe per candidate
    /// on <c>(aggregate_id, topic = ANY($2))</c>, with the row comparison resolved inside the index.
    /// With nothing held (<c>'{}'</c>) it costs the same as the query without it. While a topic is
    /// held, the scan of <c>ix_outbox_pending</c> walks past that topic's rows at the head of the
    /// backlog on every pass (measured in track C: ≈ 11 ms with 60 000 held rows, ≈ 89 ms with
    /// 600 000); the loop only runs it on a wake-up, a poll or after a batch that made progress.
    /// </para>
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
          AND NOT (m.topic = ANY($2))
          AND NOT EXISTS (
                SELECT 1
                FROM outbox.message p
                WHERE p.status = 'PENDING'
                  AND p.aggregate_id = m.aggregate_id
                  AND p.topic = ANY($2)
                  AND (p.created_at, p.id) < (m.created_at, m.id))
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

    public Task<IOutboxBatch> BeginBatchAsync(int batchSize, CancellationToken cancellationToken) =>
        BeginBatchAsync(batchSize, [], cancellationToken);

    public async Task<IOutboxBatch> BeginBatchAsync(int batchSize, IReadOnlyCollection<string> excludedTopics, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var topics = ExcludedTopicsArray(excludedTopics);

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
                command.Parameters.Add(new NpgsqlParameter<string[]> { TypedValue = topics, NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
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

    /// <summary>
    /// <c>$2</c> of <see cref="SelectBatchSql"/>: always a non-null array without null elements
    /// (empty when nothing is held), because a NULL array or element would make every
    /// <c>NOT (m.topic = ANY($2))</c> NULL and silently empty every batch.
    /// </summary>
    internal static string[] ExcludedTopicsArray(IReadOnlyCollection<string> excludedTopics)
    {
        ArgumentNullException.ThrowIfNull(excludedTopics);
        var topics = excludedTopics.ToArray();
        if (Array.Exists(topics, t => t is null))
        {
            throw new ArgumentException("Excluded topics must not contain null: NOT (topic = ANY(...)) would be NULL and every batch empty.", nameof(excludedTopics));
        }

        return topics;
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

using Npgsql;
using NpgsqlTypes;

namespace Nexus.OutboxRelay.Postgres;

/// <summary>
/// A locked batch on the leader session. Marks are buffered and sent in one round trip at commit.
/// Each UPDATE touches only the columns <c>svc_relay</c> may update
/// (<c>status, published_at, attempts, last_error</c>) and re-checks <c>status = 'PENDING'</c>
/// (defence in depth: the row is locked by this transaction, so it always holds).
/// </summary>
internal sealed class PostgresOutboxBatch : IOutboxBatch
{
    private const string MarkPublishedSql =
        "UPDATE outbox.message SET status = 'PUBLISHED', published_at = $2 WHERE id = $1 AND status = 'PENDING'";

    private const string MarkFailedSql =
        "UPDATE outbox.message SET attempts = $2, last_error = $3, status = $4 WHERE id = $1 AND status = 'PENDING'";

    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly List<NpgsqlBatchCommand> _marks = [];
    private bool _completed;

    public PostgresOutboxBatch(NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<OutboxMessage> messages)
    {
        _connection = connection;
        _transaction = transaction;
        Messages = messages;
    }

    public IReadOnlyList<OutboxMessage> Messages { get; }

    public Task ConfirmLeadershipAsync(CancellationToken cancellationToken) =>
        PostgresRelayLease.ConfirmLockAsync(_connection, _transaction, cancellationToken);

    public void MarkPublished(Guid id, DateTimeOffset publishedAt)
    {
        var command = new NpgsqlBatchCommand(MarkPublishedSql);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter<DateTimeOffset> { TypedValue = publishedAt, NpgsqlDbType = NpgsqlDbType.TimestampTz });
        _marks.Add(command);
    }

    public void MarkFailed(Guid id, int attempts, string lastError, bool terminal)
    {
        var command = new NpgsqlBatchCommand(MarkFailedSql);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id, NpgsqlDbType = NpgsqlDbType.Uuid });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = attempts, NpgsqlDbType = NpgsqlDbType.Integer });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = lastError, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = terminal ? "FAILED" : "PENDING", NpgsqlDbType = NpgsqlDbType.Text });
        _marks.Add(command);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_marks.Count > 0)
        {
            await using var batch = new NpgsqlBatch(_connection, _transaction);
            foreach (var mark in _marks)
            {
                batch.BatchCommands.Add(mark);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        _completed = true;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            // Not committed => rolled back by the transaction's dispose: every row stays PENDING
            // with its previous attempts, so nothing is lost (it may be published again: at least once).
            await _transaction.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A broken session cannot roll back; PostgreSQL aborts the transaction itself.
        catch when (!_completed)
#pragma warning restore CA1031
        {
        }
    }
}

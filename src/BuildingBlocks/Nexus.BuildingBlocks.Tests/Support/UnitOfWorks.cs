using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Npgsql;

namespace Nexus.BuildingBlocks.Tests.Support;

/// <summary>Builds real <see cref="UnitOfWork"/>s (real <see cref="OutboxWriter"/>, real Avro contracts).</summary>
internal static class UnitOfWorks
{
    /// <summary>
    /// The real outbox writer, validating payloads against <c>contracts/events/_test/</c>
    /// (where <see cref="TestEvent"/>'s <c>.avsc</c> lives).
    /// </summary>
    public static OutboxWriter RealOutboxWriter() =>
        new(new FileSystemEventSchemaProvider(RepoRoot.Find("contracts/events/_test")), new TestIdGenerator(), TimeProvider.System);

    public static UnitOfWork Create(ServiceDataSource dataSource, Guid userId, IOutboxWriter? outboxWriter = null) =>
        new(
            dataSource,
            new TestCurrentUser(userId),
            new CorrelationContext { CorrelationId = Guid.CreateVersion7() },
            outboxWriter ?? RealOutboxWriter());

    /// <summary>What a command handler does: a business write on the unit of work's own connection and transaction.</summary>
    public static async Task InsertBusinessRowAsync(IUnitOfWork uow, Guid id, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"INSERT INTO {RuntimeTestSchema.Schema}.business_row (id, name) VALUES ($1, $2)",
            uow.Connection,
            uow.Transaction);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = name });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static Task<long> CountBusinessRowsAsync(PostgresFixture db, Guid id) =>
        CountAsync(db, $"SELECT count(*) FROM {RuntimeTestSchema.Schema}.business_row WHERE id = $1", id);

    public static Task<long> CountOutboxRowsAsync(PostgresFixture db, Guid aggregateId) =>
        CountAsync(db, "SELECT count(*) FROM outbox.message WHERE aggregate_id = $1", aggregateId);

    private static async Task<long> CountAsync(PostgresFixture db, string sql, Guid id) =>
        await db.ScalarAsync<long>(sql, id);
}

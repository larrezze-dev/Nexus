using System.Text.Json;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Tests.Support;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// conventions-testing.md §3, "Comando que emite evento": (a) the event lands in
/// <c>outbox.message</c> in the same transaction as the business write; (b) if the handler
/// fails after writing, neither the row nor the event remains (conventions-events.md §3: never
/// produced to Kafka from a handler, only through the outbox in the business transaction).
/// </summary>
public sealed class OutboxAtomicityTests(PostgresFixture db) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Persistence.ServiceDataSource DataSource => db.GetServiceDataSource(RuntimeTestSchema.OwningServiceRole);

    public async ValueTask InitializeAsync() => await RuntimeTestSchema.EnsureCreatedAsync(db, Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact(DisplayName = "Outbox (a): business row and outbox.message row are committed together, in the same transaction")]
    public async Task BusinessRowAndOutboxRow_CommitInTheSameTransaction()
    {
        var id = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        await using (var uow = UnitOfWorks.Create(DataSource, userId))
        {
            await uow.BeginAsync(Ct);
            await UnitOfWorks.InsertBusinessRowAsync(uow, id, "atomic", Ct);
            uow.AddEvent(new TestEvent(id, "atomic"), aggregateVersion: 1);
            await uow.CommitAsync(Ct);
        }

        (await UnitOfWorks.CountBusinessRowsAsync(db, id)).ShouldBe(1);
        (await UnitOfWorks.CountOutboxRowsAsync(db, id)).ShouldBe(1);

        // Same transaction, proved by PostgreSQL itself: both rows were created by the same
        // top-level transaction id (xmin), not merely "both present".
        var businessXmin = await db.ScalarAsync<string>(
            $"SELECT xmin::text FROM {RuntimeTestSchema.Schema}.business_row WHERE id = $1", id);
        var outboxXmin = await db.ScalarAsync<string>(
            "SELECT xmin::text FROM outbox.message WHERE aggregate_id = $1", id);
        businessXmin.ShouldNotBeNullOrEmpty();
        outboxXmin.ShouldBe(businessXmin);

        // And the outbox row carries the unit of work's actor in its envelope headers.
        var headers = await db.ScalarAsync<string>("SELECT headers::text FROM outbox.message WHERE aggregate_id = $1", id);
        using var json = JsonDocument.Parse(headers!);
        json.RootElement.GetProperty(OutboxHeaders.ActorUserId).GetGuid().ShouldBe(userId);
    }

    [Fact(DisplayName = "Outbox (b): handler throws after writing the row and adding the event: neither row nor event remains")]
    public async Task HandlerThrowsBeforeCommit_LeavesNeitherRowNorEvent()
    {
        var id = Guid.CreateVersion7();

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await using var uow = UnitOfWorks.Create(DataSource, Guid.CreateVersion7());
            await uow.BeginAsync(Ct);
            await UnitOfWorks.InsertBusinessRowAsync(uow, id, "handler-fails", Ct);
            uow.AddEvent(new TestEvent(id, "handler-fails"), aggregateVersion: 1);
            throw new InvalidOperationException("handler failed after writing");
        });
        ex.Message.ShouldBe("handler failed after writing");

        (await UnitOfWorks.CountBusinessRowsAsync(db, id)).ShouldBe(0);
        (await UnitOfWorks.CountOutboxRowsAsync(db, id)).ShouldBe(0);
    }

    [Fact(DisplayName = "Outbox (b): failure after the outbox INSERT ran but before COMMIT rolls back both row and event")]
    public async Task FailureAfterOutboxInsert_BeforeCommit_RollsBackBoth()
    {
        var id = Guid.CreateVersion7();
        var writer = new FailAfterWriteOutboxWriter(UnitOfWorks.RealOutboxWriter());

        await using (var uow = UnitOfWorks.Create(DataSource, Guid.CreateVersion7(), writer))
        {
            await uow.BeginAsync(Ct);
            await UnitOfWorks.InsertBusinessRowAsync(uow, id, "fails-before-commit", Ct);
            uow.AddEvent(new TestEvent(id, "fails-before-commit"), aggregateVersion: 1);

            var ex = await Should.ThrowAsync<InvalidOperationException>(() => uow.CommitAsync(Ct));
            ex.Message.ShouldBe(FailAfterWriteOutboxWriter.FailureMessage);
            uow.IsActive.ShouldBeFalse();
        }

        // The failure really happened after the outbox row had been inserted in the transaction.
        writer.OutboxInsertsSeenInTransaction.ShouldBe(1);

        (await UnitOfWorks.CountBusinessRowsAsync(db, id)).ShouldBe(0);
        (await UnitOfWorks.CountOutboxRowsAsync(db, id)).ShouldBe(0);
    }

    /// <summary>
    /// Runs the real writer (so the outbox INSERT executes inside the transaction), records how
    /// many rows this transaction inserted into <c>outbox.message</c>, then fails before the
    /// unit of work reaches COMMIT.
    /// </summary>
    private sealed class FailAfterWriteOutboxWriter(IOutboxWriter inner) : IOutboxWriter
    {
        public const string FailureMessage = "failure after the outbox write, before COMMIT";

        public long OutboxInsertsSeenInTransaction { get; private set; }

        public async Task WriteAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            IReadOnlyList<PendingDomainEvent> events,
            OutboxEnvelopeContext context,
            CancellationToken cancellationToken)
        {
            // Business roles hold INSERT only on outbox.message (no SELECT), so the insert is
            // observed through the backend's own statistics counter instead. Measured as a delta
            // around the real write: since PostgreSQL 15 that counter also holds earlier,
            // not-yet-flushed transactions of the same pooled backend, so its absolute value is
            // not "this transaction only".
            var before = await ReadOutboxInsertCounterAsync(connection, transaction, cancellationToken);
            await inner.WriteAsync(connection, transaction, events, context, cancellationToken);
            var after = await ReadOutboxInsertCounterAsync(connection, transaction, cancellationToken);
            OutboxInsertsSeenInTransaction = after - before;

            throw new InvalidOperationException(FailureMessage);
        }

        private static async Task<long> ReadOutboxInsertCounterAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            // stats_fetch_consistency = none: read live values, not a snapshot cached for the transaction.
            await using var command = new NpgsqlCommand(
                "SET LOCAL stats_fetch_consistency = none; " +
                "SELECT coalesce((SELECT n_tup_ins FROM pg_stat_xact_user_tables " +
                "WHERE schemaname = 'outbox' AND relname = 'message'), 0)",
                connection,
                transaction);
            return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
    }
}

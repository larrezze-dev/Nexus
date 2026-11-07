using Microsoft.Extensions.Logging.Abstractions;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Npgsql;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>
/// The batch algorithm of conventions-events.md §4.3–§4.5 against the real <c>outbox.message</c>,
/// run as role <c>svc_relay</c> (SELECT + UPDATE on status/published_at/attempts/last_error only).
/// Kafka is replaced by a scripted <see cref="IEventPublisher"/>; the real producer is covered by
/// <see cref="KafkaPublishingTests"/>.
/// </summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class OutboxBatchTests(PostgresFixture db)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "§4.3: svc_relay locks a batch FOR UPDATE SKIP LOCKED with its column-only UPDATE grant, in (created_at, id) order, up to the batch size")]
    public async Task BatchQuery_RunsAsSvcRelay_InOrder_Limited()
    {
        await OutboxRows.ClearAsync(db);
        var aggregate = Guid.CreateVersion7();
        await OutboxRows.InsertAsync(db, aggregate, 3, T0.AddSeconds(3));
        var first = await OutboxRows.InsertAsync(db, aggregate, 1, T0.AddSeconds(1));
        var second = await OutboxRows.InsertAsync(db, aggregate, 2, T0.AddSeconds(2));
        await OutboxRows.InsertAsync(db, aggregate, 0, T0, status: "PUBLISHED");

        await using var lease = await OutboxRows.AcquireAsync(db);
        await using var batch = await lease.BeginBatchAsync(2, CancellationToken.None);

        batch.Messages.Select(m => m.Id).ShouldBe([first, second]);
    }

    [Fact(DisplayName = "§4.3: rows locked by another transaction are skipped (SKIP LOCKED), not waited for")]
    public async Task LockedRows_AreSkipped()
    {
        await OutboxRows.ClearAsync(db);
        var locked = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0);
        var free = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0.AddSeconds(1));

        await using var other = await db.Admin.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var otherTx = await other.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var lockRow = new NpgsqlCommand("SELECT 1 FROM outbox.message WHERE id = $1 FOR UPDATE", other, otherTx))
        {
            lockRow.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = locked });
            await lockRow.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var lease = await OutboxRows.AcquireAsync(db);
        await using var batch = await lease.BeginBatchAsync(10, CancellationToken.None);
        batch.Messages.Select(m => m.Id).ShouldBe([free]);
    }

    [Fact(DisplayName = "§4.5: a published row becomes PUBLISHED with published_at from the injected TimeProvider")]
    public async Task Success_MarksPublished()
    {
        await OutboxRows.ClearAsync(db);
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0);
        var now = new DateTimeOffset(2026, 11, 7, 12, 34, 56, 789, TimeSpan.Zero);

        var outcome = await RunBatchAsync(new ScriptedPublisher(_ => PublishResult.Success), new FixedTimeProvider(now));

        outcome.Published.ShouldBe(1);
        var row = await OutboxRows.GetAsync(db, id);
        row.Status.ShouldBe("PUBLISHED");
        row.PublishedAt.ShouldBe(now);
        row.Attempts.ShouldBe(0);
    }

    [Fact(DisplayName = "§4.5: each failure increments attempts and records last_error; the 10th makes the row FAILED and counts nexus_outbox_failed_total")]
    public async Task Failures_ReachFailedAtMaxAttempts()
    {
        await OutboxRows.ClearAsync(db);
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0);
        var publisher = new ScriptedPublisher(_ => PublishResult.Failure("boom: schema missing", PublishFailureKind.Row));
        using var meters = new TestMeterFactory();
        using var failedCounter = new FailedCounterListener(meters);

        for (var attempt = 1; attempt <= 9; attempt++)
        {
            await RunBatchAsync(publisher, TimeProvider.System, meters);
            var row = await OutboxRows.GetAsync(db, id);
            row.Status.ShouldBe("PENDING", $"attempt {attempt}");
            row.Attempts.ShouldBe(attempt);
            row.LastError.ShouldBe("boom: schema missing");
            row.PublishedAt.ShouldBeNull();
        }

        failedCounter.Total.ShouldBe(0);

        await RunBatchAsync(publisher, TimeProvider.System, meters);
        var failed = await OutboxRows.GetAsync(db, id);
        failed.Status.ShouldBe("FAILED");
        failed.Attempts.ShouldBe(10);
        failedCounter.Total.ShouldBe(1);

        // A FAILED row is never selected again.
        var after = await RunBatchAsync(publisher, TimeProvider.System, meters);
        after.Selected.ShouldBe(0);
        publisher.Attempted.Count.ShouldBe(10);
    }

    [Fact(DisplayName = "EV-R3: after a failure in a batch, the aggregate's later rows stay untouched (PENDING, attempts unchanged); other aggregates are still published")]
    public async Task FailureInBatch_HoldsBackSameAggregateOnly()
    {
        await OutboxRows.ClearAsync(db);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var a1 = await OutboxRows.InsertAsync(db, a, 1, T0.AddMilliseconds(1));
        var b1 = await OutboxRows.InsertAsync(db, b, 1, T0.AddMilliseconds(2));
        var a2 = await OutboxRows.InsertAsync(db, a, 2, T0.AddMilliseconds(3));
        var b2 = await OutboxRows.InsertAsync(db, b, 2, T0.AddMilliseconds(4));
        var a3 = await OutboxRows.InsertAsync(db, a, 3, T0.AddMilliseconds(5));

        var publisher = new ScriptedPublisher(m => m.Id == a1 ? PublishResult.Failure("a1 rejected", PublishFailureKind.Row) : PublishResult.Success);
        var outcome = await RunBatchAsync(publisher, TimeProvider.System);

        publisher.Attempted.Select(m => m.Id).ShouldBe([a1, b1, b2], "a2 and a3 must not be attempted after a1 failed");
        outcome.ShouldBe(new BatchOutcome(Selected: 5, Published: 2, Failed: 1, StoppedOnTransientFailure: false));

        (await OutboxRows.GetAsync(db, a1)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(1));
        (await OutboxRows.GetAsync(db, a2)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(0), r => r.LastError.ShouldBeNull());
        (await OutboxRows.GetAsync(db, a3)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(0));
        (await OutboxRows.GetAsync(db, b1)).Status.ShouldBe("PUBLISHED");
        (await OutboxRows.GetAsync(db, b2)).Status.ShouldBe("PUBLISHED");

        // Next batch: a1 first again, then a2, a3 in order.
        var retry = new ScriptedPublisher(_ => PublishResult.Success);
        await RunBatchAsync(retry, TimeProvider.System);
        retry.Attempted.Select(m => m.Id).ShouldBe([a1, a2, a3]);
    }

    [Fact(DisplayName = "EV-R3: a FAILED row holds back every later PENDING row of its aggregate in later batches; other aggregates and earlier rows are unaffected")]
    public async Task FailedRow_BlocksLaterRowsOfItsAggregate()
    {
        await OutboxRows.ClearAsync(db);
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        await OutboxRows.InsertAsync(db, a, 1, T0.AddMilliseconds(1), status: "PUBLISHED");
        var a2Failed = await OutboxRows.InsertAsync(db, a, 2, T0.AddMilliseconds(2), status: "FAILED", attempts: 10);
        var a3 = await OutboxRows.InsertAsync(db, a, 3, T0.AddMilliseconds(3));
        var b1 = await OutboxRows.InsertAsync(db, b, 1, T0.AddMilliseconds(4));

        var publisher = new ScriptedPublisher(_ => PublishResult.Success);
        await RunBatchAsync(publisher, TimeProvider.System);

        publisher.Attempted.Select(m => m.Id).ShouldBe([b1]);
        (await OutboxRows.GetAsync(db, a3)).Status.ShouldBe("PENDING");

        // Operator fixes the cause and resets the FAILED row: publication resumes in order.
        await using (var reset = db.Admin.CreateCommand("UPDATE outbox.message SET status = 'PENDING', attempts = 0 WHERE id = $1"))
        {
            reset.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = a2Failed });
            await reset.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var resumed = new ScriptedPublisher(_ => PublishResult.Success);
        await RunBatchAsync(resumed, TimeProvider.System);
        resumed.Attempted.Select(m => m.Id).ShouldBe([a2Failed, a3]);
    }

    [Fact(DisplayName = "§4.4: a transient failure (Kafka/Schema Registry down) stops the batch; the failed row keeps PENDING with attempts+1 and the rest are untouched")]
    public async Task TransientFailure_StopsBatch_RowsStayPending()
    {
        await OutboxRows.ClearAsync(db);
        var first = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0.AddMilliseconds(1));
        var second = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0.AddMilliseconds(2));

        var publisher = new ScriptedPublisher(_ => PublishResult.Failure("Schema Registry unavailable", PublishFailureKind.InfraGlobal));
        var outcome = await RunBatchAsync(publisher, TimeProvider.System);

        outcome.StoppedOnTransientFailure.ShouldBeTrue();
        publisher.Attempted.Select(m => m.Id).ShouldBe([first]);
        (await OutboxRows.GetAsync(db, first)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(1));
        (await OutboxRows.GetAsync(db, second)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(0));
    }

    [Fact(DisplayName = "§4.5: a batch disposed without commit (e.g. leader session lost) leaves every row as it was")]
    public async Task UncommittedBatch_RollsBack()
    {
        await OutboxRows.ClearAsync(db);
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0);

        await using (var lease = await OutboxRows.AcquireAsync(db))
        {
            await using var batch = await lease.BeginBatchAsync(10, CancellationToken.None);
            batch.MarkPublished(id, T0);
        }

        (await OutboxRows.GetAsync(db, id)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.PublishedAt.ShouldBeNull());
    }

    private async Task<BatchOutcome> RunBatchAsync(IEventPublisher publisher, TimeProvider time, TestMeterFactory? meters = null)
    {
        using var ownMeters = meters is null ? new TestMeterFactory() : null;
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters ?? ownMeters!), time, maxAttempts: 10, NullLogger.Instance);
        await using var lease = await OutboxRows.AcquireAsync(db);
        await using var batch = await lease.BeginBatchAsync(500, CancellationToken.None);
        return await processor.ProcessAsync(batch, CancellationToken.None);
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

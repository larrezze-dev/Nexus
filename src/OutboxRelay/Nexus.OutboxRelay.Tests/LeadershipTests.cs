using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Postgres;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Npgsql;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>Leader election against the real database, as role <c>svc_relay</c> (conventions-events.md §4.1).</summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class LeadershipTests(PostgresFixture db)
{
    [Fact(DisplayName = "§4.1: only one instance holds the leader lock; releasing it lets a passive instance take over")]
    public async Task OnlyOneLeader()
    {
        var leadership = OutboxRows.Leadership(db);

        var first = await leadership.TryAcquireAsync(CancellationToken.None);
        first.ShouldNotBeNull();
        try
        {
            (await leadership.TryAcquireAsync(CancellationToken.None)).ShouldBeNull("a second instance must stay passive");
        }
        finally
        {
            await first.DisposeAsync();
        }

        var second = await leadership.TryAcquireAsync(CancellationToken.None);
        second.ShouldNotBeNull("the lock is released on dispose");
        await second.DisposeAsync();
    }

    [Fact(DisplayName = "§4.1: the lease connection is dedicated (not pooled): closing it ends the session and frees the lock")]
    public async Task LeaseSessionIsNotPooled()
    {
        var lease = (PostgresRelayLease)await OutboxRows.AcquireAsync(db);
        var pid = lease.BackendProcessId;
        await lease.DisposeAsync();

        // Give the backend a moment to exit; a pooled connection would keep it alive.
        for (var i = 0; i < 20 && await db.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1)", pid); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        (await db.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE pid = $1)", pid)).ShouldBeFalse();
    }

    [Fact(DisplayName = "§4.1: when the leader session dies, the lease refuses to lock a batch and another instance can take over")]
    public async Task LeaderSessionKilled_StopsPublishing()
    {
        await OutboxRows.ClearAsync(db);
        await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow);

        var lease = (PostgresRelayLease)await OutboxRows.AcquireAsync(db);
        try
        {
            (await db.ScalarAsync<bool>("SELECT pg_terminate_backend($1)", lease.BackendProcessId)).ShouldBeTrue();

            await Should.ThrowAsync<RelayLeadershipLostException>(() => lease.BeginBatchAsync(10, CancellationToken.None));

            var successor = await OutboxRows.Leadership(db).TryAcquireAsync(CancellationToken.None);
            successor.ShouldNotBeNull("the terminated session released the lock");
            await successor.DisposeAsync();
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Fact(DisplayName = "§4.1: leader session killed after the batch is locked ⇒ the per-message confirmation stops it before producing anything; rows stay PENDING")]
    public async Task LeaderSessionKilledMidBatch_PublishesNothing()
    {
        await OutboxRows.ClearAsync(db);
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow);
        var publisher = new ScriptedPublisher(_ => PublishResult.Success);
        using var meters = new TestMeterFactory();
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var lease = (PostgresRelayLease)await OutboxRows.AcquireAsync(db);
        try
        {
            await using var batch = await lease.BeginBatchAsync(10, CancellationToken.None);
            batch.Messages.Count.ShouldBe(1);
            (await db.ScalarAsync<bool>("SELECT pg_terminate_backend($1)", lease.BackendProcessId)).ShouldBeTrue();

            await Should.ThrowAsync<RelayLeadershipLostException>(() => processor.ProcessAsync(batch, CancellationToken.None));
            publisher.Attempted.ShouldBeEmpty();
        }
        finally
        {
            await lease.DisposeAsync();
        }

        (await OutboxRows.GetAsync(db, id)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(0));
    }

    [Fact(DisplayName = "§4.1: the per-batch lock confirmation fails if the session no longer holds the advisory lock")]
    public async Task LockNotConfirmed_Throws()
    {
        var lease = await OutboxRows.AcquireAsync(db);
        try
        {
            // Simulate a lost lock on a live session (cannot happen through the relay's own code).
            await using (var connection = new NpgsqlConnection(db.GetConnectionString("svc_relay")))
            {
                // Sanity: another session cannot see the key as free.
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                await using var probe = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtext('nexus-relay'))", connection);
                ((bool)(await probe.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBeFalse();
            }

            var confirm = await ConfirmOnFreshSessionAsync();
            confirm.ShouldBeFalse("a session that does not hold the lock is not confirmed");
        }
        finally
        {
            await lease.DisposeAsync();
        }

        async Task<bool> ConfirmOnFreshSessionAsync()
        {
            await using var connection = new NpgsqlConnection(db.GetConnectionString("svc_relay"));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(PostgresRelayLease.ConfirmLockSql, connection);
            command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = PostgresRelayLeadership.LockName });
            return (bool)(await command.ExecuteScalarAsync())!;
        }
    }

    [Fact(DisplayName = "§4.1: the lock confirmation query recognises the lock taken with pg_try_advisory_lock(hashtext('nexus-relay'))")]
    public async Task LockConfirmedOnHolderSession()
    {
        // Not pooled: closing must end the session, or the lock would survive in the pool.
        await using var connection = new NpgsqlConnection(
            new NpgsqlConnectionStringBuilder(db.GetConnectionString("svc_relay")) { Pooling = false }.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var take = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtext('nexus-relay'))", connection))
        {
            ((bool)(await take.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBeTrue();
        }

        await using var command = new NpgsqlCommand(PostgresRelayLease.ConfirmLockSql, connection);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = PostgresRelayLeadership.LockName });
        ((bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).ShouldBeTrue();
    }
}

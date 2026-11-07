using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>The hosted loop against the real database (Kafka faked by a scripted publisher).</summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class RelayWorkerTests(PostgresFixture db)
{
    [Fact(DisplayName = "§4.2: the leader is woken by NOTIFY outbox_new (poll interval set to 60 s) and publishes the new row")]
    public async Task NotifyWakesTheLeader()
    {
        await OutboxRows.ClearAsync(db);
        var publisher = new ScriptedPublisher(_ => PublishResult.Success);
        using var worker = CreateWorker(publisher, pollIntervalMs: 60_000);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // Let it become leader and run its first (empty) batch, then go idle on LISTEN.
            await WaitUntilAsync(async () => await db.ScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'nexus-relay' AND state = 'idle')"));

            var stopwatch = Stopwatch.StartNew();
            var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow);
            await WaitUntilAsync(async () => (await OutboxRows.GetAsync(db, id)).Status == "PUBLISHED");
            stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "woken by NOTIFY, not by the 60 s poll");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact(DisplayName = "§4.1: a second worker stays passive while the first holds the lock; it publishes nothing")]
    public async Task SecondWorkerStaysPassive()
    {
        await OutboxRows.ClearAsync(db);
        var activePublisher = new ScriptedPublisher(_ => PublishResult.Success);
        var passivePublisher = new ScriptedPublisher(_ => PublishResult.Success);
        using var active = CreateWorker(activePublisher, pollIntervalMs: 100);
        await active.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => await db.ScalarAsync<long>("SELECT count(*) FROM pg_stat_activity WHERE application_name = 'nexus-relay'") == 1);

        using var passive = CreateWorker(passivePublisher, pollIntervalMs: 100);
        await passive.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 20; i++)
            {
                await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow);
            }

            await WaitUntilAsync(async () => await db.ScalarAsync<long>("SELECT count(*) FROM outbox.message WHERE status = 'PENDING'") == 0);
            activePublisher.Attempted.Count.ShouldBe(20);
            passivePublisher.Attempted.ShouldBeEmpty();
        }
        finally
        {
            await passive.StopAsync(CancellationToken.None);
            await active.StopAsync(CancellationToken.None);
        }
    }

    private RelayWorker CreateWorker(IEventPublisher publisher, int pollIntervalMs) =>
        new(
            OutboxRows.Leadership(db),
            publisher,
            new RelayMetrics(new TestMeterFactory()),
            Options.Create(new RelayOptions { PollIntervalMs = pollIntervalMs, LeaderRetrySeconds = 1 }),
            TimeProvider.System,
            NullLogger<RelayWorker>.Instance);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(50);
        }
    }
}

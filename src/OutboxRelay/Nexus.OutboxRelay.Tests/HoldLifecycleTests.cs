using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>Lifecycle of the in-memory holds across leases (adopted from the 5th review pass).</summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class HoldLifecycleTests(PostgresFixture db)
{
    [Fact(DisplayName = "N1: holds do not survive a lease loss: if another leader drained the probe aggregate meanwhile, regaining leadership publishes the topic's other held aggregates at once")]
    public async Task StaleHolds_AfterLeaseFlip_AreCleared()
    {
        await OutboxRows.ClearAsync(db);
        const string T1 = "nexus.test.review.t1";
        var topicUp = false;
        var publisher = new ScriptedPublisher(m =>
            m.Topic != T1 || Volatile.Read(ref topicUp) ? PublishResult.Success
            : PublishResult.TopicUnreachable("topic missing", DateTimeOffset.UtcNow.AddSeconds(5)));
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var (p, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var p1 = await OutboxRows.InsertAsync(db, p, 1, t0, "{}", T1);
        var b1 = await OutboxRows.InsertAsync(db, b, 1, t0.AddMilliseconds(1), "{}", T1);

        using var meters = new TestMeterFactory();
        using var worker = new RelayWorker(new NoLeadership(), publisher, new RelayMetrics(meters),
            Options.Create(new RelayOptions { PollIntervalMs = 200 }), TimeProvider.System, NullLogger<RelayWorker>.Instance);

        // Leadership 1: both aggregates get held (p is the probe of T1).
        await using (var lease1 = await OutboxRows.AcquireAsync(db))
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            await RunQuietlyAsync(worker, lease1, cts.Token);
        }

        (await OutboxRows.GetAsync(db, b1)).Status.ShouldBe("PENDING");

        // Meanwhile another leader published p1 (the topic was created).
        Volatile.Write(ref topicUp, true);
        await db.ScalarAsync<int>($"WITH u AS (UPDATE outbox.message SET status = 'PUBLISHED', published_at = now() WHERE id = '{p1}' RETURNING 1) SELECT count(*)::int FROM u");

        // Leadership 2 of the same process: b1 is published at once (well before the 5 s probe).
        await using var lease2 = await OutboxRows.AcquireAsync(db);
        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var run = RunQuietlyAsync(worker, lease2, cts2.Token);
        try
        {
            await Poll.UntilAsync(async () => (await OutboxRows.GetAsync(db, b1)).Status == "PUBLISHED", TimeSpan.FromSeconds(2), "b1 published right after regaining leadership");
        }
        finally
        {
            await cts2.CancelAsync();
            await run;
        }
    }

    private static async Task RunQuietlyAsync(RelayWorker worker, IRelayLease lease, CancellationToken token)
    {
        try
        {
            await worker.RunAsLeaderAsync(lease, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class NoLeadership : IRelayLeadership
    {
        public Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken) => Task.FromResult<IRelayLease?>(null);
    }
}

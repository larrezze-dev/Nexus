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

/// <summary>The leader loop with an in-memory lease (no database).</summary>
public sealed class RelayWorkerLoopTests
{
    [Fact(DisplayName = "N5: the back-off warning names the failure class and its cause (no payload)")]
    public async Task BackOffLog_NamesCause()
    {
        using var cts = new CancellationTokenSource();
        var logger = new CapturingLogger<RelayWorker>();
        var publisher = new ScriptedPublisher(_ =>
        {
            cts.Cancel(); // one batch only
            return PublishResult.Failure("Schema Registry unavailable looking up subject 'nexus.test.x-value': HttpRequestException.", PublishFailureKind.InfraGlobal);
        });
        using var meters = new TestMeterFactory();
        using var worker = new RelayWorker(
            new NeverLeadership(),
            publisher,
            new RelayMetrics(meters),
            Options.Create(new RelayOptions { PollIntervalMs = 100 }),
            TimeProvider.System,
            logger);

        await worker.RunAsLeaderAsync(new FakeLease(FakeBatch.Message(Guid.CreateVersion7(), 1)), cts.Token);

        logger.Messages.ShouldContain(m =>
            m.Contains("InfraGlobal", StringComparison.Ordinal)
            && m.Contains("Schema Registry unavailable looking up subject 'nexus.test.x-value': HttpRequestException.", StringComparison.Ordinal)
            && m.Contains("next batch in 100 ms", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "H1: a full batch whose rows are all deferred (topic unreachable) does not trigger an immediate re-drain: the loop waits for the poll interval instead of spinning")]
    public async Task FullBatchOfDeferredRows_DoesNotSpin()
    {
        var topicDown = Enumerable.Range(0, 3).Select(_ => FakeBatch.Message(Guid.CreateVersion7(), 1)).ToArray();
        var lease = new FakeLease(topicDown) { WaitDelay = TimeSpan.FromMilliseconds(1) };
        var publisher = new ScriptedPublisher(_ => PublishResult.Deferred("topic missing (cached)", DateTimeOffset.UtcNow.AddSeconds(5)));
        using var meters = new TestMeterFactory();
        using var worker = new RelayWorker(
            new NeverLeadership(),
            publisher,
            new RelayMetrics(meters),
            Options.Create(new RelayOptions { BatchSize = 3, PollIntervalMs = 200 }),
            TimeProvider.System,
            NullLogger<RelayWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await worker.RunAsLeaderAsync(lease, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        lease.Batches.ShouldBeLessThanOrEqualTo(8, "about one batch per 200 ms poll interval, not a busy loop");
    }

    private sealed class NeverLeadership : IRelayLeadership
    {
        public Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken) => Task.FromResult<IRelayLease?>(null);
    }

    private sealed class FakeLease(params OutboxMessage[] messages) : IRelayLease
    {
        public Task<bool> WaitForWakeUpAsync(TimeSpan timeout, CancellationToken cancellationToken) => WaitAsync(timeout, cancellationToken);

        public int Batches { get; private set; }

        public TimeSpan WaitDelay { get; init; } = TimeSpan.Zero;

        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (WaitDelay > TimeSpan.Zero)
            {
                await Task.Delay(timeout, cancellationToken);
            }

            return false;
        }

        public Task<IOutboxBatch> BeginBatchAsync(int batchSize, IReadOnlyCollection<Guid> excludedAggregates, CancellationToken cancellationToken)
        {
            Batches++;
            return Task.FromResult<IOutboxBatch>(new FakeBatch(messages));
        }

        public Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken) => Task.FromResult(new PendingStats(messages.Length, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

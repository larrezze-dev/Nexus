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

        lease.Batches.ShouldBeLessThanOrEqualTo(10, "about one batch per 200 ms poll interval, not a busy loop");
    }

    [Fact(DisplayName = "H1(a): a full batch that held back a NEW topic drains on at once (progress through the backlog) instead of waiting for the poll interval")]
    public async Task FullBatchOfNewHolds_DrainsWithoutWaiting()
    {
        // Batches 1-5: 3 rows of 3 new aggregates each on a new unreachable topic; then empty.
        var lease = new FakeLease
        {
            WaitDelay = TimeSpan.FromMilliseconds(1),
            Next = n => n <= 5 ? Enumerable.Range(0, 3).Select(_ => FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = $"nexus.test.down-{n}.created" }).ToArray() : [],
        };
        var publisher = new ScriptedPublisher(_ => PublishResult.Deferred("topic missing (cached)", DateTimeOffset.UtcNow.AddMinutes(5)));
        using var meters = new TestMeterFactory();
        using var worker = new RelayWorker(
            new NeverLeadership(),
            publisher,
            new RelayMetrics(meters),
            Options.Create(new RelayOptions { BatchSize = 3, PollIntervalMs = 2_000 }),
            TimeProvider.System,
            NullLogger<RelayWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        try
        {
            await worker.RunAsLeaderAsync(lease, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        lease.Batches.ShouldBeGreaterThanOrEqualTo(6, "the 5 backlog batches and the first empty one run back to back, not one per 2 s poll");
    }

    [Fact(DisplayName = "H1(b): probing several held topics with full backlogs drains back to back (a probe that finds the topic still unreachable is progress), so the rows behind them are not starved by the probes")]
    public async Task ProbesOfSeveralTopics_DoNotStarveRowsBehind()
    {
        string[] down = ["nexus.test.down-1.created", "nexus.test.down-2.created", "nexus.test.down-3.created"];
        var lease = new TopicAwareLease(down);
        var publisher = NegativeCachingPublisher(down, ttl: TimeSpan.FromMilliseconds(300));
        using var meters = new TestMeterFactory();
        using var worker = new RelayWorker(
            new NeverLeadership(),
            publisher,
            new RelayMetrics(meters),
            Options.Create(new RelayOptions { BatchSize = 3, PollIntervalMs = 1_000 }),
            TimeProvider.System,
            NullLogger<RelayWorker>.Instance);

        // Every poll (1 s) finds the three topics' probes due (TTL 300 ms) and their backlogs at the
        // head. Waiting a poll after each probe would starve the healthy rows for good.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3.5));
        try
        {
            await worker.RunAsLeaderAsync(lease, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        lease.HealthyServed.ShouldBeGreaterThanOrEqualTo(3, "a healthy batch after the probes of every poll (t = 0, 1, 2, 3 s), not only the first");
        publisher.Attempted.Count(m => !down.Contains(m.Topic)).ShouldBe(lease.HealthyServed);
    }

    /// <summary>A publisher that, like <c>KafkaEventPublisher</c>, caches an unreachable topic for <paramref name="ttl"/>.</summary>
    private static ScriptedPublisher NegativeCachingPublisher(IReadOnlyCollection<string> down, TimeSpan ttl)
    {
        var cache = new Dictionary<string, DateTimeOffset>();
        return new ScriptedPublisher(m =>
        {
            if (!down.Contains(m.Topic))
            {
                return PublishResult.Success;
            }

            var until = DateTimeOffset.UtcNow + ttl;
            cache[m.Topic] = until;
            return PublishResult.TopicUnreachable($"topic '{m.Topic}' missing", until);
        })
        {
            Defer = m => cache.TryGetValue(m.Topic, out var until) && DateTimeOffset.UtcNow < until ? PublishResult.Deferred("topic missing (cached)", until) : null,
        };
    }

    /// <summary>
    /// Models the batch query's topic exclusion: the rows of the first topic of <c>down</c> that is
    /// not excluded fill the batch (their backlog is at the head); when every one is excluded, one
    /// healthy row comes back.
    /// </summary>
    private sealed class TopicAwareLease(string[] down) : IRelayLease
    {
        public int HealthyServed { get; private set; }

        public async Task<bool> WaitForWakeUpAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            await Task.Delay(timeout, cancellationToken);
            return false;
        }

        public Task<IOutboxBatch> BeginBatchAsync(int batchSize, IReadOnlyCollection<string> excludedTopics, CancellationToken cancellationToken)
        {
            if (down.FirstOrDefault(t => !excludedTopics.Contains(t)) is { } topic)
            {
                return Task.FromResult<IOutboxBatch>(new FakeBatch([.. Enumerable.Range(0, batchSize).Select(_ => FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = topic })]));
            }

            HealthyServed++;
            return Task.FromResult<IOutboxBatch>(new FakeBatch(FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = "nexus.test.healthy.created" }));
        }

        public Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken) => Task.FromResult(new PendingStats(0, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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

        /// <summary>When set, builds each batch (argument: 1-based batch number) instead of repeating the fixed messages.</summary>
        public Func<int, OutboxMessage[]>? Next { get; init; }

        public Task<IOutboxBatch> BeginBatchAsync(int batchSize, IReadOnlyCollection<string> excludedTopics, CancellationToken cancellationToken)
        {
            Batches++;
            return Task.FromResult<IOutboxBatch>(new FakeBatch(Next?.Invoke(Batches) ?? messages));
        }

        public Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken) => Task.FromResult(new PendingStats(messages.Length, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

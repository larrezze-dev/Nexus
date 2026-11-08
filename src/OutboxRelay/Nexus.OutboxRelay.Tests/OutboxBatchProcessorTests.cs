using Microsoft.Extensions.Logging.Abstractions;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>Pure tests of the per-batch algorithm (no database, no Kafka).</summary>
public sealed class OutboxBatchProcessorTests
{
    [Fact(DisplayName = "EV-R3: with interleaved aggregates, a failure of one aggregate only skips that aggregate's later rows")]
    public async Task InterleavedAggregates()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var rows = new[]
        {
            FakeBatch.Message(a, 1), FakeBatch.Message(b, 1), FakeBatch.Message(c, 1),
            FakeBatch.Message(a, 2), FakeBatch.Message(b, 2), FakeBatch.Message(c, 2),
        };
        var batch = new FakeBatch(rows);
        var publisher = new ScriptedPublisher(m => m.AggregateId == b && m.AggregateVersion == 1 ? PublishResult.Failure("x", PublishFailureKind.Row) : PublishResult.Success);

        using var meters = new TestMeterFactory();
        var outcome = await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        publisher.Attempted.Select(m => (m.AggregateId, m.AggregateVersion)).ShouldBe([(a, 1L), (b, 1L), (c, 1L), (a, 2L), (c, 2L)]);
        batch.Published.Count.ShouldBe(4);
        batch.Failed.Keys.ShouldBe([rows[1].Id]);
        batch.Committed.ShouldBeTrue();
        outcome.Failed.ShouldBe(1);
    }

    [Fact(DisplayName = "§4.5: attempts continue from the row's stored value; terminal at MaxAttempts")]
    public async Task AttemptsFromStoredValue()
    {
        var row = FakeBatch.Message(Guid.CreateVersion7(), 1, attempts: 9);
        var batch = new FakeBatch(row);
        using var meters = new TestMeterFactory();
        using var counter = new FailedCounterListener(meters);

        await new OutboxBatchProcessor(new ScriptedPublisher(_ => PublishResult.Failure("x", PublishFailureKind.Row)), new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        batch.Failed[row.Id].ShouldBe((10, "x", true));
        counter.Total.ShouldBe(1);
    }

    [Theory(DisplayName = "§4.5 + D-H1: a transient failure never makes a row FAILED, even at or beyond MaxAttempts; attempts and last_error are still recorded")]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(57)]
    public async Task TransientFailure_NeverTerminal(int storedAttempts)
    {
        var row = FakeBatch.Message(Guid.CreateVersion7(), 1, attempts: storedAttempts);
        var batch = new FakeBatch(row);
        using var meters = new TestMeterFactory();
        using var counter = new FailedCounterListener(meters);

        var outcome = await new OutboxBatchProcessor(new ScriptedPublisher(_ => PublishResult.Failure("registry down", PublishFailureKind.InfraGlobal)), new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        batch.Failed[row.Id].ShouldBe((storedAttempts + 1, "registry down", false));
        counter.Total.ShouldBe(0);
        outcome.StoppedOnTransientFailure.ShouldBeTrue();
    }

    [Fact(DisplayName = "§4.5 + D-H1: after an outage pushed attempts past MaxAttempts, the next row-specific failure is terminal at once")]
    public async Task RowFailureAfterOutage_IsTerminal()
    {
        var row = FakeBatch.Message(Guid.CreateVersion7(), 1, attempts: 14);
        var batch = new FakeBatch(row);
        using var meters = new TestMeterFactory();
        using var counter = new FailedCounterListener(meters);

        await new OutboxBatchProcessor(new ScriptedPublisher(_ => PublishResult.Failure("bad payload", PublishFailureKind.Row)), new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        batch.Failed[row.Id].ShouldBe((15, "bad payload", true));
        counter.Total.ShouldBe(1);
    }

    [Theory(DisplayName = "TopicUnreachable: only that aggregate is skipped, other aggregates continue, never FAILED (even far beyond MaxAttempts), counted as errors{kind=topic}")]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(57)]
    public async Task TopicUnreachable_SkipsAggregate_NeverTerminal(int storedAttempts)
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var a1 = FakeBatch.Message(a, 1, attempts: storedAttempts) with { Topic = "nexus.test.missing.created" };
        var a2 = FakeBatch.Message(a, 2) with { Topic = "nexus.test.missing.created" };
        var b1 = FakeBatch.Message(b, 1);
        var batch = new FakeBatch(a1, b1, a2);
        var publisher = new ScriptedPublisher(m => m.AggregateId == a ? PublishResult.Failure("topic 'nexus.test.missing.created' does not exist", PublishFailureKind.TopicUnreachable) : PublishResult.Success);
        using var meters = new TestMeterFactory();
        using var counter = new FailedCounterListener(meters);
        using var errors = new ErrorKindListener(meters);

        var outcome = await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        outcome.StoppedOnTransientFailure.ShouldBeFalse();
        publisher.Attempted.Select(m => m.Id).ShouldBe([a1.Id, b1.Id], "a2 skipped (EV-R3), b1 still attempted");
        batch.Failed[a1.Id].ShouldBe((storedAttempts + 1, "topic 'nexus.test.missing.created' does not exist", false));
        batch.Published.Keys.ShouldBe([b1.Id]);
        counter.Total.ShouldBe(0);
        errors.Count(RelayMetrics.ErrorKindTopic).ShouldBe(1);
    }

    [Fact(DisplayName = "TopicUnreachable (negative cache): a Deferred row holds back its aggregate without any mark, log or metric; other aggregates continue")]
    public async Task Deferred_SkipsAggregate_WithoutAttempt()
    {
        var a = Guid.CreateVersion7();
        var a1 = FakeBatch.Message(a, 1, attempts: 4);
        var a2 = FakeBatch.Message(a, 2);
        var b1 = FakeBatch.Message(Guid.CreateVersion7(), 1);
        var batch = new FakeBatch(a1, b1, a2);
        var publisher = new ScriptedPublisher(m => m.AggregateId == a ? PublishResult.Deferred("topic missing (cached)", DateTimeOffset.UtcNow.AddSeconds(5)) : PublishResult.Success);
        using var meters = new TestMeterFactory();
        using var errors = new ErrorKindListener(meters);

        var outcome = await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        publisher.Attempted.Select(m => m.Id).ShouldBe([a1.Id, b1.Id]);
        batch.Failed.ShouldBeEmpty("no attempts + 1, no last_error");
        batch.Published.Keys.ShouldBe([b1.Id]);
        errors.Count(RelayMetrics.ErrorKindTopic).ShouldBe(0);
        outcome.ShouldSatisfyAllConditions(o => o.Deferred.ShouldBe(1), o => o.Failed.ShouldBe(0), o => o.StoppedOnTransientFailure.ShouldBeFalse());
    }

    [Fact(DisplayName = "H1(b): holds are per topic and do not expire while the topic is unreachable: at the probe time the topic leaves the exclusion list; still unreachable ⇒ held again (new probe time); the topic answers ⇒ released")]
    public async Task Holds_SurviveWhileTopicUnreachable_ReleasedWhenItAnswers()
    {
        const string Down = "nexus.test.down.created";
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var topicUp = false;
        var probes = 0;
        var publisher = new ScriptedPublisher(m =>
        {
            if (m.Topic != Down || topicUp)
            {
                return PublishResult.Success;
            }

            if (m.AggregateId == a)
            {
                probes++;
                return PublishResult.TopicUnreachable("topic missing", time.GetUtcNow().AddSeconds(5));
            }

            return PublishResult.Deferred("topic missing (cached)", time.GetUtcNow().AddSeconds(5));
        });
        using var meters = new TestMeterFactory();
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters), time, 10, NullLogger.Instance);
        OutboxMessage Row(Guid aggregate) => FakeBatch.Message(aggregate, 1) with { Topic = Down };

        var first = await processor.ProcessAsync(new FakeBatch(Row(a), Row(b), FakeBatch.Message(c, 1)), CancellationToken.None);
        first.NewHolds.ShouldBe(1, "one topic, however many of its aggregates the batch skipped");
        processor.Held.Current().ShouldBe([Down]);
        processor.Held.Count.ShouldBe(1);

        time.Advance(TimeSpan.FromSeconds(5));
        processor.Held.Current().ShouldBeEmpty("probe due: the topic leaves the exclusion list so the batch reaches its oldest rows");
        processor.Held.IsHeld(Down).ShouldBeTrue("probing, still held");

        // The probe finds the topic still missing: held again with the new probe time, which is
        // progress (the next batch excludes it again) — once per topic per batch.
        var probe = await processor.ProcessAsync(new FakeBatch(Row(a), Row(b)), CancellationToken.None);
        probe.NewHolds.ShouldBe(1, "the probing topic re-enters the exclusion list");
        probes.ShouldBe(2);
        processor.Held.Current().ShouldBe([Down]);
        time.Advance(TimeSpan.FromSeconds(4));
        processor.Held.Current().ShouldBe([Down], "extended to the new probe time, not expired");

        // Next probe: the topic answers ⇒ released.
        time.Advance(TimeSpan.FromSeconds(1));
        processor.Held.Current().ShouldBeEmpty();
        topicUp = true;
        var answered = await processor.ProcessAsync(new FakeBatch(Row(a), Row(b)), CancellationToken.None);
        answered.ShouldSatisfyAllConditions(o => o.Published.ShouldBe(2), o => o.NewHolds.ShouldBe(0));
        processor.Held.Current().ShouldBeEmpty();
        processor.Held.Count.ShouldBe(0);
        processor.Held.IsHeld(Down).ShouldBeFalse();
    }

    [Fact(DisplayName = "H1(b): HeldTopics.Hold — true for a new topic, false while already excluded, true once when a probing topic is still unreachable; an earlier probe time never shortens a hold")]
    public void HeldTopics_HoldSemantics()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var held = new HeldTopics(time);
        var t5 = time.GetUtcNow().AddSeconds(5);

        held.Hold("t1", t5).ShouldBeTrue("new topic");
        held.Hold("t1", t5).ShouldBeFalse("already excluded");
        held.Hold("t1", t5.AddSeconds(-3)).ShouldBeFalse();
        time.Advance(TimeSpan.FromSeconds(2));
        held.Current().ShouldBe(["t1"], "an earlier probe time did not shorten the hold");

        time.Advance(TimeSpan.FromSeconds(3));
        held.Current().ShouldBeEmpty("probe due");
        held.Hold("t1", time.GetUtcNow().AddSeconds(5)).ShouldBeTrue("probing and still unreachable: excluded again");
        held.Hold("t1", time.GetUtcNow().AddSeconds(5)).ShouldBeFalse("once per batch");
        held.Current().ShouldBe(["t1"]);
        held.Count.ShouldBe(1);

        held.ReleaseTopic("t1");
        held.Count.ShouldBe(0);
        held.Current().ShouldBeEmpty();
        held.Hold("t1", time.GetUtcNow().AddSeconds(5)).ShouldBeTrue("released, then unreachable again: a new hold");
    }

    [Fact(DisplayName = "H1(b) fairness + LRU: a probe batch leaves out ONE due topic, by a single LRU key (time of the last try, else probe time; then name); the next batch excludes every held topic; a re-held topic goes to the back; a topic tried without an answer is retried before topics that became due after that try")]
    public void HeldTopics_OneTopicPerProbeBatch_LeastRecentlyProbedFirst()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var held = new HeldTopics(time);
        var t0 = time.GetUtcNow();
        IReadOnlyCollection<string> Next()
        {
            time.Advance(TimeSpan.FromMilliseconds(10)); // each batch takes a little time
            return held.Current();
        }

        held.Hold("t2", t0.AddSeconds(4));
        held.Hold("t1", t0.AddSeconds(5));
        held.Hold("t0", t0.AddSeconds(5));
        held.Hold("t9", t0.AddSeconds(60));
        held.HasUntriedDueTopic().ShouldBeFalse("nothing due yet");
        time.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromMilliseconds(10));

        Next().ShouldBe(["t0", "t1", "t9"], ignoreOrder: true, "probe batch: only t2 (oldest probe time) is left out");
        held.Hold("t2", time.GetUtcNow().AddSeconds(5)).ShouldBeTrue("t2 probed, still unreachable: back of the queue");
        held.HasUntriedDueTopic().ShouldBeTrue("t0 and t1 are still due and untried: drain on");

        Next().ShouldBe(["t0", "t1", "t2", "t9"], ignoreOrder: true, "fairness: no probe right after a probe batch");
        held.ReleaseUnprobeableTopics([]);
        held.Count.ShouldBe(4, "nothing is probing in a fairness batch: the defensive release cannot misfire");

        Next().ShouldBe(["t1", "t2", "t9"], ignoreOrder: true, "same probe time: t0 before t1 (ordinal)");
        // t0's row is not reached (e.g. the batch stopped on an InfraGlobal failure): no Hold, no release.
        Next();
        Next().ShouldBe(["t0", "t2", "t9"], ignoreOrder: true, "t1 (probe time 5.00 s) before t0 (tried at 5.02 s)");
        held.Hold("t1", time.GetUtcNow().AddSeconds(5));
        held.HasUntriedDueTopic().ShouldBeFalse("t0 is due but already tried: no more drains for it");
        Next();
        Next().ShouldBe(["t1", "t2", "t9"], ignoreOrder: true, "t0 is retried (the only due topic), tried again at 5.06 s");

        // t2 and t1 are due again (probe times 10.00 s and 10.04 s); t0 was last tried at 5.06 s.
        time.Advance(TimeSpan.FromSeconds(5));
        held.HasUntriedDueTopic().ShouldBeTrue();
        Next().ShouldBe(["t0", "t1", "t2", "t9"], ignoreOrder: true, "fairness after the last probe batch");
        Next().ShouldBe(["t1", "t2", "t9"], ignoreOrder: true, "single LRU key: t0 (tried at 5.06 s) before the untried t2 and t1 — under saturation untried topics never run out, so a priority for them would starve t0");
        Next();
        Next().ShouldBe(["t0", "t1", "t9"], ignoreOrder: true, "then t2 (10.00 s)");
        Next();
        Next().ShouldBe(["t0", "t2", "t9"], ignoreOrder: true, "then t1 (10.04 s)");

        held.Clear();
        held.Hold("t4", time.GetUtcNow());
        held.Current().ShouldBeEmpty("Clear also resets the alternation: a new lease may probe at once");
    }

    [Fact(DisplayName = "H1(b): HeldTopics.Current never returns null nor null elements, and only the topics whose probe time is in the future")]
    public void HeldTopics_Current_IsNeverNull()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var held = new HeldTopics(time);
        held.Current().ShouldNotBeNull().ShouldBeEmpty();
        held.Hold("t1", time.GetUtcNow().AddSeconds(5));
        held.Hold("t2", time.GetUtcNow().AddSeconds(10));
        held.Hold("t3", time.GetUtcNow());
        held.Current().ShouldBe(["t1", "t2"], ignoreOrder: true, "t3's probe time is now: it is probing");
        held.Current().ShouldAllBe(t => t != null);
        Should.Throw<ArgumentNullException>(() => held.Hold(null!, time.GetUtcNow()));
    }

    [Fact(DisplayName = "H1: rows the publisher defers without I/O (topic negative-cached) skip the leader-lock round trip and are not published; other rows still confirm the lock")]
    public async Task TryDefer_SkipsLockConfirmation()
    {
        var down = Enumerable.Range(0, 4).Select(_ => FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = "nexus.test.down.created" }).ToArray();
        var ok = FakeBatch.Message(Guid.CreateVersion7(), 1);
        var batch = new FakeBatch([.. down, ok]);
        var publisher = new ScriptedPublisher(_ => PublishResult.Success)
        {
            Defer = m => m.Topic == "nexus.test.down.created" ? PublishResult.Deferred("topic missing (cached)", DateTimeOffset.UtcNow.AddSeconds(5)) : null,
        };
        using var meters = new TestMeterFactory();

        var outcome = await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        batch.Confirmations.ShouldBe(1, "only the produced row confirms the lock");
        publisher.Attempted.ShouldBe([ok]);
        outcome.ShouldSatisfyAllConditions(o => o.Deferred.ShouldBe(4), o => o.NewHolds.ShouldBe(1, "one topic"), o => o.Published.ShouldBe(1));
    }

    [Fact(DisplayName = "N2: the probe row failing as Row (topic reachable, bad payload) releases the topic")]
    public async Task ProbeRowFailure_ReleasesTopic()
    {
        const string T = "nexus.test.review.row";
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var up = false;
        var publisher = new ScriptedPublisher(m => up ? PublishResult.Failure("bad payload", PublishFailureKind.Row)
            : m.AggregateId == a ? PublishResult.TopicUnreachable("missing", time.GetUtcNow().AddSeconds(5))
            : PublishResult.Deferred("missing", time.GetUtcNow().AddSeconds(5)));
        using var meters = new TestMeterFactory();
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters), time, 10, NullLogger.Instance);
        OutboxMessage Row(Guid g) => FakeBatch.Message(g, 1) with { Topic = T };

        await processor.ProcessAsync(new FakeBatch(Row(a), Row(b)), CancellationToken.None);
        processor.Held.IsHeld(T).ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(5));
        processor.Held.Current().ShouldBeEmpty();
        up = true;
        await processor.ProcessAsync(new FakeBatch(Row(a)), CancellationToken.None);

        processor.Held.IsHeld(T).ShouldBeFalse("a Row failure proves the topic answered: it must be released");
        processor.Held.Current().ShouldBeEmpty();
    }

    [Fact(DisplayName = "N1: defensive release — the topic that a partial batch left out to be probed is released if none of its rows is in it; kept if one is; a batch with no probing topic releases nothing")]
    public void UnprobeableTopic_IsReleased()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var held = new HeldTopics(time);
        held.Hold("t1", time.GetUtcNow().AddSeconds(5));
        held.Hold("t2", time.GetUtcNow().AddSeconds(5));
        held.Hold("t3", time.GetUtcNow().AddSeconds(60));

        held.Current();
        held.ReleaseUnprobeableTopics([]);
        held.Count.ShouldBe(3, "no probe is due yet");

        time.Advance(TimeSpan.FromSeconds(5));
        held.Current().ShouldBe(["t2", "t3"], ignoreOrder: true, "t1 is probing");
        held.ReleaseUnprobeableTopics([FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = "t2" }]);
        held.IsHeld("t1").ShouldBeFalse("t1 was left out and has nothing selectable: released");
        held.IsHeld("t2").ShouldBeTrue("t2 was not left out by this batch: absent by design, even though due");
        held.IsHeld("t3").ShouldBeTrue();

        held.Current().ShouldBe(["t2", "t3"], ignoreOrder: true, "fairness batch");
        held.ReleaseUnprobeableTopics([]);
        held.IsHeld("t2").ShouldBeTrue("a batch with no probing topic releases nothing");

        held.Current().ShouldBe(["t3"], "t2 is probing");
        held.ReleaseUnprobeableTopics([FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = "t2" }]);
        held.IsHeld("t2").ShouldBeTrue("t2's row is in the batch: the preflight decides");

        // The probing topic is consumed: a second call without a new Current() releases nothing.
        held.ReleaseUnprobeableTopics([]);
        held.IsHeld("t2").ShouldBeTrue();
    }

    [Fact(DisplayName = "N1: RunAsLeaderAsync starts by clearing the holds of a previous lease")]
    public async Task NewLease_ClearsHolds()
    {
        using var meters = new TestMeterFactory();
        var publisher = new ScriptedPublisher(_ => PublishResult.Success);
        using var worker = new RelayWorker(new EmptyLeadership(), publisher, new RelayMetrics(meters),
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { PollIntervalMs = 50 }), TimeProvider.System, NullLogger<RelayWorker>.Instance);
        // Hold far in the future: neither the probe nor the defensive release can drop it.
        var processor = worker.ProcessorForTests;
        processor.Held.Hold("t1", DateTimeOffset.UtcNow.AddHours(1));
        processor.Held.Hold("t2", DateTimeOffset.UtcNow.AddHours(1));
        var lease = new EmptyLease();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        try
        {
            await worker.RunAsLeaderAsync(lease, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        processor.Held.Count.ShouldBe(0);
        lease.FirstExclusion.ShouldNotBeNull().ShouldBeEmpty("the very first batch of the lease excludes nothing");
    }

    [Fact(DisplayName = "N1: the leader loop applies the defensive release: when a held topic's rows disappear, a partial batch releases the topic instead of holding it forever")]
    public async Task LeaderLoop_ReleasesUnprobeableTopic()
    {
        const string T = "nexus.test.gone.created";
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var publisher = new ScriptedPublisher(m => m.AggregateId == a
            ? PublishResult.TopicUnreachable("missing", DateTimeOffset.UtcNow.AddMilliseconds(300))
            : PublishResult.Deferred("missing", DateTimeOffset.UtcNow.AddMilliseconds(300)));
        using var meters = new TestMeterFactory();
        using var worker = new RelayWorker(new EmptyLeadership(), publisher, new RelayMetrics(meters),
            Microsoft.Extensions.Options.Options.Create(new RelayOptions { PollIntervalMs = 50 }), TimeProvider.System, NullLogger<RelayWorker>.Instance);
        // Batch 1: a and b on T, T gets held. Then T's rows are gone (e.g. purged by an operator):
        // every later batch comes back empty.
        var lease = new EmptyLease { First = [FakeBatch.Message(a, 1) with { Topic = T }, FakeBatch.Message(b, 1) with { Topic = T }] };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try
        {
            await worker.RunAsLeaderAsync(lease, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        worker.ProcessorForTests.Held.IsHeld(T).ShouldBeFalse("T must not stay held forever once it has nothing to probe");
    }

    private sealed class EmptyLeadership : IRelayLeadership
    {
        public Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken) => Task.FromResult<IRelayLease?>(null);
    }

    private sealed class EmptyLease : IRelayLease
    {
        public async Task<bool> WaitForWakeUpAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            await Task.Delay(timeout, cancellationToken);
            return false;
        }

        /// <summary>When set, the first batch; every later batch is empty.</summary>
        public OutboxMessage[]? First { get; init; }

        /// <summary>The exclusion list of the first batch.</summary>
        public IReadOnlyCollection<string>? FirstExclusion { get; private set; }

        private int _batches;

        public Task<IOutboxBatch> BeginBatchAsync(int batchSize, IReadOnlyCollection<string> excludedTopics, CancellationToken cancellationToken)
        {
            var n = _batches++;
            if (n == 0)
            {
                FirstExclusion = [.. excludedTopics];
            }

            return Task.FromResult<IOutboxBatch>(First is null
                ? new FakeBatch(FakeBatch.Message(Guid.CreateVersion7(), 1))
                : new FakeBatch(n == 0 ? First : []));
        }

        public Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken) => Task.FromResult(new PendingStats(0, null));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact(DisplayName = "InfraGlobal: the batch stops at once and the outcome carries the cause (for the back-off log)")]
    public async Task InfraGlobal_StopsBatch_WithCause()
    {
        var batch = new FakeBatch(FakeBatch.Message(Guid.CreateVersion7(), 1), FakeBatch.Message(Guid.CreateVersion7(), 1));
        var publisher = new ScriptedPublisher(_ => PublishResult.Failure("Schema Registry unavailable looking up subject 's': HttpRequestException.", PublishFailureKind.InfraGlobal));
        using var meters = new TestMeterFactory();

        var outcome = await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        publisher.Attempted.Count.ShouldBe(1);
        outcome.StoppedOnTransientFailure.ShouldBeTrue();
        outcome.StopCause.ShouldBe("Schema Registry unavailable looking up subject 's': HttpRequestException.");
    }

    [Fact(DisplayName = "§4.4: on shutdown no further row is attempted, and what was done is committed")]
    public async Task StopsBetweenRowsOnShutdown()
    {
        using var cts = new CancellationTokenSource();
        var batch = new FakeBatch(FakeBatch.Message(Guid.CreateVersion7(), 1), FakeBatch.Message(Guid.CreateVersion7(), 1));
        var publisher = new ScriptedPublisher(_ =>
        {
            cts.Cancel();
            return PublishResult.Success;
        });

        using var meters = new TestMeterFactory();
        await new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, cts.Token);

        publisher.Attempted.Count.ShouldBe(1);
        batch.Published.Count.ShouldBe(1);
        batch.Committed.ShouldBeTrue();
    }

    [Fact(DisplayName = "§4.1: leadership lost mid-batch ⇒ no further row is published and nothing is committed")]
    public async Task LeadershipLostMidBatch()
    {
        var batch = new FakeBatch(FakeBatch.Message(Guid.CreateVersion7(), 1), FakeBatch.Message(Guid.CreateVersion7(), 1), FakeBatch.Message(Guid.CreateVersion7(), 1))
        {
            LeadershipChecksBeforeLoss = 1,
        };
        var publisher = new ScriptedPublisher(_ => PublishResult.Success);

        using var meters = new TestMeterFactory();
        await Should.ThrowAsync<RelayLeadershipLostException>(() =>
            new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
                .ProcessAsync(batch, CancellationToken.None));

        publisher.Attempted.Count.ShouldBe(1);
        batch.Committed.ShouldBeFalse();
    }

    [Fact(DisplayName = "§4.5: last_error is truncated and stripped of NUL characters")]
    public void LastErrorTruncation()
    {
        OutboxBatchProcessor.Truncate(new string('x', 5000)).Length.ShouldBe(OutboxBatchProcessor.LastErrorMaxLength);
        OutboxBatchProcessor.Truncate("a\0b").ShouldBe("a b");
        OutboxBatchProcessor.Truncate(null).ShouldNotBeNullOrWhiteSpace();
    }
}

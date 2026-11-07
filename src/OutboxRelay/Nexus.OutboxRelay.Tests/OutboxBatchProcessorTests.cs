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

    [Fact(DisplayName = "H1: a TopicUnreachable result (counted or deferred) holds its aggregate until the topic's negative cache expires; other aggregates are not held")]
    public async Task TopicUnreachable_HoldsAggregateUntilExpiry()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero));
        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var until = time.GetUtcNow().AddSeconds(5);
        var publisher = new ScriptedPublisher(m =>
            m.AggregateId == a ? PublishResult.TopicUnreachable("topic missing", until)
            : m.AggregateId == b ? PublishResult.Deferred("topic missing (cached)", until)
            : PublishResult.Success);
        using var meters = new TestMeterFactory();
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters), time, 10, NullLogger.Instance);

        await processor.ProcessAsync(new FakeBatch(FakeBatch.Message(a, 1), FakeBatch.Message(b, 1), FakeBatch.Message(c, 1)), CancellationToken.None);

        processor.Held.Current().ShouldBe([a, b], ignoreOrder: true);
        time.Advance(TimeSpan.FromSeconds(5));
        processor.Held.Current().ShouldBeEmpty("expired with the negative cache: the next batch selects them and the preflight re-runs");
    }

    [Fact(DisplayName = "H1: HeldAggregates is bounded to 10 000 aggregates")]
    public void HeldAggregates_Bounded()
    {
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var held = new HeldAggregates(time);
        for (var i = 0; i < HeldAggregates.Capacity + 50; i++)
        {
            held.Hold(Guid.CreateVersion7(), time.GetUtcNow().AddSeconds(5));
        }

        held.Current().Count.ShouldBe(HeldAggregates.Capacity);
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

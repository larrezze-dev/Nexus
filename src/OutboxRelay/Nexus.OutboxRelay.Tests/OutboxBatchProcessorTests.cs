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
        var publisher = new ScriptedPublisher(m => m.AggregateId == b && m.AggregateVersion == 1 ? PublishResult.Failure("x", false) : PublishResult.Success);

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

        await new OutboxBatchProcessor(new ScriptedPublisher(_ => PublishResult.Failure("x", false)), new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance)
            .ProcessAsync(batch, CancellationToken.None);

        batch.Failed[row.Id].ShouldBe((10, "x", true));
        counter.Total.ShouldBe(1);
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

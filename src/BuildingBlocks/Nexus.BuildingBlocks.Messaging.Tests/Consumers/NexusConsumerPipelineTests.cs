using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.BuildingBlocks.Testing;
using Shouldly;
using static Nexus.BuildingBlocks.Messaging.Tests.Consumers.ConsumerTestKit;
using static Nexus.BuildingBlocks.Messaging.Tests.Inbox.InboxTestHost;

namespace Nexus.BuildingBlocks.Messaging.Tests.Consumers;

/// <summary>
/// The full consumer pipeline of <see cref="NexusConsumer{TEvent}"/> (conventions-events.md §7) against a
/// real Kafka + Schema Registry and a real Postgres (inbox + effect in one transaction, as
/// <c>svc_operations</c>), with the generated <c>LegalEntityCreated</c> Avro class.
/// </summary>
[Collection(KafkaPostgresFixtures.Name)]
public sealed class NexusConsumerPipelineTests(KafkaFixture kafka, PostgresFixture db) : IAsyncLifetime
{
    private ServiceProvider _host = null!;
    private KafkaDlqPublisher _dlq = null!;

    public async ValueTask InitializeAsync()
    {
        await EnsureSchemaAsync(db, TestContext.Current.CancellationToken);
        _host = Build(db);
        _dlq = new KafkaDlqPublisher(kafka.BootstrapServers, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        _dlq.Dispose();
        await _host.DisposeAsync();
    }

    private NexusConsumerRuntime Runtime(ConsumerHealthRegistry? health = null, IDlqPublisher? dlq = null) =>
        ConsumerTestKit.Runtime(_host, kafka, dlq ?? _dlq, health);

    private static ConsumerLoopState State(ConsumerHealthRegistry health, string group) =>
        health.Snapshot().Single(c => c.GroupId == group).State;

    private Task<long> EffectsAsync(string group, Guid eventId) => CountEffectRowsAsync(db, group, eventId);

    private static Task WriteEffect(TestLegalEntityConsumer c, EventEnvelope e, IServiceProvider sp, CancellationToken ct) =>
        WriteEffectRowAsync(sp, c.GroupId, e.EventId, ct);

    [Fact(DisplayName = "conventions-events §7: a handler that fails all 4 attempts (1 + 3 retries) sends exactly one record to {topic}.DLQ with the original key, bytes and headers plus the 3 nexus-dlq-* headers, and the next record is processed")]
    public async Task HandlerFailsAllAttempts_GoesToDlq_AndNextIsProcessed()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var failingLe = LegalEntity("LE-FAIL");
        var failing = Envelope(failingLe.id);
        var nextLe = LegalEntity("LE-NEXT");
        var next = Envelope(nextLe.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(), topic, "dlq-test", (le, e, sp, ct) =>
            e.EventId == failing.EventId
                ? throw new InvalidOperationException("boom")
                : WriteEffect(consumer, e, sp, ct));

        var failingBytes = await ProduceAsync(kafka, topic, failingLe, failing);
        await ProduceAsync(kafka, topic, nextLe, next);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(async () => await EffectsAsync(consumer.GroupId, next.EventId) == 1, "the next record is processed");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        consumer.Invocations[failing.EventId].ShouldBe(4);
        (await EffectsAsync(consumer.GroupId, failing.EventId)).ShouldBe(0);
        (await CountInboxRowsAsync(db, consumer.GroupId, failing.EventId)).ShouldBe(0);

        var dlq = kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 2, TimeSpan.FromSeconds(3));
        dlq.Count.ShouldBe(1);
        var record = dlq[0].Message;
        record.Key.ShouldBe(failingLe.id.ToString("D"));
        record.Value.ShouldBe(failingBytes);
        Header(record.Headers, EventHeaderNames.EventId).ShouldBe(failing.EventId.ToString("D"));
        Header(record.Headers, DlqHeaders.Error).ShouldContain("boom");
        Header(record.Headers, DlqHeaders.Consumer).ShouldBe(consumer.GroupId);
        DateTimeOffset.Parse(Header(record.Headers, DlqHeaders.FailedAt), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        // Both offsets committed: the DLQ'd record is not redelivered.
        kafka.CommittedOffset(consumer.GroupId, topic).ShouldBe(2);
    }

    [Fact(DisplayName = "conventions-events §7: a handler that fails 3 times and succeeds on the 4th attempt (last retry) is processed once and NOT sent to the DLQ")]
    public async Task HandlerFailsThreeTimesThenSucceeds_NotDeadLettered()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var le = LegalEntity("LE-FLAKY");
        var envelope = Envelope(le.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(), topic, "flaky-test", (_, e, sp, ct) =>
            consumer.Invocations[e.EventId] <= 3
                ? throw new InvalidOperationException($"transient failure {consumer.Invocations[e.EventId]}")
                : WriteEffect(consumer, e, sp, ct));

        await ProduceAsync(kafka, topic, le, envelope);
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(kafka.CommittedOffset(consumer.GroupId, topic) == 1), "the offset is committed");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        consumer.Invocations[envelope.EventId].ShouldBe(4);
        (await EffectsAsync(consumer.GroupId, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, consumer.GroupId, envelope.EventId)).ShouldBe(1);
        kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 1, TimeSpan.FromSeconds(2)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "NexusConsumer: a non-fatal poll error (subscribed topic does not exist, auto-create off) keeps the loop alive and Degraded; once the topic exists it consumes")]
    public async Task UnknownTopic_LoopStaysAlive_ThenConsumes()
    {
        var topic = NewTopic(); // NOT created
        var health = new ConsumerHealthRegistry(TimeProvider.System);
        var le = LegalEntity("LE-LATE-TOPIC");
        var envelope = Envelope(le.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(health), topic, "unknown-topic-test", (_, e, sp, ct) => WriteEffect(consumer, e, sp, ct));
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(State(health, consumer.GroupId) == ConsumerLoopState.Degraded), "the loop reports Degraded");

            // Wait more than 2 x DegradedQuietPeriod (5 s): with no partitions assigned (the topic does not
            // exist) the loop must NEVER go back to Running just because poll errors were quiet for a while.
            var until = DateTime.UtcNow + (2 * NexusConsumer<ch.nussli.nexus.org.LegalEntityCreated>.DegradedQuietPeriod) + TimeSpan.FromSeconds(2);
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
                consumer.ExecuteTask!.IsCompleted.ShouldBeFalse("a non-fatal poll error must not end the loop (nor stop the host)");
                State(health, consumer.GroupId).ShouldBe(ConsumerLoopState.Degraded, "no assignment: still Degraded after the quiet period");
            }

            await kafka.CreateTopicWithDlqAsync(topic);
            await ProduceAsync(kafka, topic, le, envelope);

            await WaitUntilAsync(async () => await EffectsAsync(consumer.GroupId, envelope.EventId) == 1, "the record is processed once the topic exists");
            State(health, consumer.GroupId).ShouldBe(ConsumerLoopState.Running);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        consumer.ExecuteTask!.IsFaulted.ShouldBeFalse();
    }

    [Fact(DisplayName = "NexusConsumer: if the DLQ cannot be written the consumer pauses (Unhealthy, nothing committed, next record not consumed), retries, and resumes once the DLQ exists; the host is not stopped and nothing is lost")]
    public async Task DlqUnavailable_PausesRetriesAndResumes()
    {
        var topic = NewTopic();
        await kafka.CreateTopicsAsync(topic); // the .DLQ is missing and auto-create is off
        var health = new ConsumerHealthRegistry(TimeProvider.System);
        using var shortTimeoutDlq = new KafkaDlqPublisher(kafka.BootstrapServers, TimeProvider.System, TimeSpan.FromSeconds(2));
        var dlq = new CountingDlqPublisher(shortTimeoutDlq);
        var failingLe = LegalEntity("LE-FAIL");
        var failing = Envelope(failingLe.id);
        var nextLe = LegalEntity("LE-NEXT");
        var next = Envelope(nextLe.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(health, dlq), topic, "dlq-down-test", (_, e, sp, ct) =>
            e.EventId == failing.EventId ? throw new InvalidOperationException("boom") : WriteEffect(consumer, e, sp, ct));

        var failingBytes = await ProduceAsync(kafka, topic, failingLe, failing);
        await ProduceAsync(kafka, topic, nextLe, next);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(State(health, consumer.GroupId) == ConsumerLoopState.DlqUnavailable), "the loop reports DlqUnavailable");
            await WaitUntilAsync(() => Task.FromResult(dlq.Failures >= 3), "the DLQ publish is retried");

            consumer.ExecuteTask!.IsCompleted.ShouldBeFalse("a DLQ failure must not end the loop (nor stop the host)");
            consumer.Invocations.ContainsKey(next.EventId).ShouldBeFalse("consumption is paused while the DLQ is down");
            kafka.CommittedOffset(consumer.GroupId, topic).ShouldBeLessThan(0, "nothing is committed while the record is not in the DLQ");

            await kafka.CreateTopicsAsync(DlqHeaders.TopicFor(topic));

            await WaitUntilAsync(async () => await EffectsAsync(consumer.GroupId, next.EventId) == 1, "consumption resumes");
            await WaitUntilAsync(() => Task.FromResult(kafka.CommittedOffset(consumer.GroupId, topic) == 2), "both offsets committed");
            State(health, consumer.GroupId).ShouldBe(ConsumerLoopState.Running);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        consumer.Invocations[failing.EventId].ShouldBe(4, "the handler is not retried again while the DLQ publish is");
        var dead = kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 2, TimeSpan.FromSeconds(3));
        dead.Count.ShouldBe(1);
        dead[0].Message.Value.ShouldBe(failingBytes);
        Header(dead[0].Message.Headers, EventHeaderNames.EventId).ShouldBe(failing.EventId.ToString("D"));
    }

    [Fact(DisplayName = "NexusConsumer: a rebalance while paused on a DLQ outage does not reprocess the in-flight record nor dead-letter it twice")]
    public async Task DlqUnavailable_RebalanceWhilePaused_NoReprocessingNoDuplicate()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var dlq = new ToggleDlqPublisher(_dlq);
        var health = new ConsumerHealthRegistry(TimeProvider.System);
        var failingLe = LegalEntity("LE-FAIL");
        var failing = Envelope(failingLe.id);
        var nextLe = LegalEntity("LE-NEXT");
        var next = Envelope(nextLe.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(health, dlq), topic, "rebalance-while-paused-test", (_, e, sp, ct) =>
            e.EventId == failing.EventId ? throw new InvalidOperationException("boom") : WriteEffect(consumer, e, sp, ct));
        await ProduceAsync(kafka, topic, failingLe, failing);
        await ProduceAsync(kafka, topic, nextLe, next);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(State(health, consumer.GroupId) == ConsumerLoopState.DlqUnavailable), "the loop is paused");

            // A second member joins the group (revoke/assign) and then leaves (another revoke/assign).
            using (var other = new ConsumerBuilder<Ignore, Ignore>(new ConsumerConfig
                   {
                       BootstrapServers = kafka.BootstrapServers,
                       GroupId = consumer.GroupId,
                       EnableAutoCommit = false,
                       AutoOffsetReset = AutoOffsetReset.Earliest,
                   }).Build())
            {
                other.Subscribe(topic);
                var until = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < until)
                {
                    other.Consume(TimeSpan.FromMilliseconds(200));
                }

                other.Close();
            }

            await Task.Delay(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            dlq.Up = true;
            await WaitUntilAsync(async () => await EffectsAsync(consumer.GroupId, next.EventId) == 1, "the next record is processed");
            await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        var dead = kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 3, TimeSpan.FromSeconds(3));
        var info = $"invocations={consumer.Invocations[failing.EventId]} dlqRecords={dead.Count} publishes={dlq.Publishes}";
        consumer.Invocations[failing.EventId].ShouldBe(4, info);
        dead.Count.ShouldBe(1, info);
        kafka.CommittedOffset(consumer.GroupId, topic).ShouldBe(2);
    }

    [Fact(DisplayName = "NexusConsumer: a Degraded loop returns to Running on an idle topic once polls succeed (no record needed)")]
    public async Task Degraded_ClearedOnIdleTopic()
    {
        var topic = NewTopic(); // NOT created yet
        var health = new ConsumerHealthRegistry(TimeProvider.System);
        var consumer = Consumer(Runtime(health), topic, "degraded-idle-test", (_, _, _, _) => Task.CompletedTask);
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(State(health, consumer.GroupId) == ConsumerLoopState.Degraded), "the loop reports Degraded");
            await kafka.CreateTopicWithDlqAsync(topic);
            await Task.Delay(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            State(health, consumer.GroupId).ShouldBe(ConsumerLoopState.Running, "the topic exists and polls succeed, though no record arrived");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    [Fact(DisplayName = "NexusConsumer: on cancellation while the DLQ is down the loop stops cleanly (Stopped, not Faulted) without committing")]
    public async Task DlqUnavailable_CancellationStopsCleanlyWithoutCommit()
    {
        var topic = NewTopic();
        await kafka.CreateTopicsAsync(topic);
        var health = new ConsumerHealthRegistry(TimeProvider.System);
        var dlq = new CountingDlqPublisher(new AlwaysFailingDlqPublisher());
        var le = LegalEntity("LE-FAIL");
        var envelope = Envelope(le.id);

        var consumer = Consumer(Runtime(health, dlq), topic, "dlq-down-cancel-test", (_, _, _, _) => throw new InvalidOperationException("boom"));
        await ProduceAsync(kafka, topic, le, envelope);
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => Task.FromResult(dlq.Failures >= 2), "the DLQ publish is retried");
        await consumer.StopAsync(CancellationToken.None);

        consumer.ExecuteTask!.IsFaulted.ShouldBeFalse();
        State(health, consumer.GroupId).ShouldBe(ConsumerLoopState.Stopped);
        kafka.CommittedOffset(consumer.GroupId, topic).ShouldBeLessThan(0);
    }

    [Fact(DisplayName = "conventions-testing §3 Consumidor: the same event_id delivered twice (two Kafka records) produces a single effect")]
    public async Task SameEventIdTwice_SingleEffect()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var le = LegalEntity();
        var envelope = Envelope(le.id);
        var sentinelLe = LegalEntity("LE-SENTINEL");
        var sentinel = Envelope(sentinelLe.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(), topic, "dedup-test", (_, e, sp, ct) => WriteEffect(consumer, e, sp, ct));

        await ProduceAsync(kafka, topic, le, envelope);
        await ProduceAsync(kafka, topic, le, envelope);
        await ProduceAsync(kafka, topic, sentinelLe, sentinel);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(async () => await EffectsAsync(consumer.GroupId, sentinel.EventId) == 1, "the sentinel is processed");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        (await EffectsAsync(consumer.GroupId, envelope.EventId)).ShouldBe(1);
        (await CountInboxRowsAsync(db, consumer.GroupId, envelope.EventId)).ShouldBe(1);
        consumer.Invocations[envelope.EventId].ShouldBe(1);
        kafka.CommittedOffset(consumer.GroupId, topic).ShouldBe(3);
        kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 1, TimeSpan.FromSeconds(2)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "conventions-events §7: undeserializable bytes and a record without envelope headers go to the DLQ untouched and the consumer continues")]
    public async Task PoisonRecords_GoToDlq_AndConsumerContinues()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var garbage = Encoding.UTF8.GetBytes("this is not Confluent Avro");
        var garbageEnvelope = Envelope(Guid.CreateVersion7());
        var noHeadersLe = LegalEntity("LE-NOHEADERS");
        var goodLe = LegalEntity("LE-GOOD");
        var good = Envelope(goodLe.id);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(), topic, "poison-test", (_, e, sp, ct) => WriteEffect(consumer, e, sp, ct));

        await ProduceRawAsync(kafka, topic, "poison-key", garbage, Headers(garbageEnvelope));
        // A valid Avro value whose envelope headers are missing.
        var validBytes = await SerializeAsync(kafka, topic, noHeadersLe);
        await ProduceRawAsync(kafka, topic, "no-headers-key", validBytes, new Headers());
        await ProduceAsync(kafka, topic, goodLe, good);

        await consumer.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(async () => await EffectsAsync(consumer.GroupId, good.EventId) == 1, "the good record is processed");
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        consumer.Invocations.ContainsKey(garbageEnvelope.EventId).ShouldBeFalse();
        var dlq = kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 3, TimeSpan.FromSeconds(3));
        dlq.Count.ShouldBe(2);

        dlq[0].Message.Key.ShouldBe("poison-key");
        dlq[0].Message.Value.ShouldBe(garbage);
        Header(dlq[0].Message.Headers, EventHeaderNames.EventId).ShouldBe(garbageEnvelope.EventId.ToString("D"));
        Header(dlq[0].Message.Headers, DlqHeaders.Consumer).ShouldBe(consumer.GroupId);

        dlq[1].Message.Key.ShouldBe("no-headers-key");
        dlq[1].Message.Value.ShouldBe(validBytes);
        Header(dlq[1].Message.Headers, DlqHeaders.Error).ShouldContain(nameof(InvalidEventEnvelopeException));

        kafka.CommittedOffset(consumer.GroupId, topic).ShouldBe(3);
    }

    [Fact(DisplayName = "conventions-events §7: on cancellation (host shutdown) the in-flight record is neither committed, nor sent to the DLQ, nor recorded in the inbox")]
    public async Task Cancellation_DoesNotCommit()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var le = LegalEntity();
        var envelope = Envelope(le.id);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        TestLegalEntityConsumer consumer = null!;
        consumer = Consumer(Runtime(), topic, "cancel-test", async (_, e, sp, ct) =>
        {
            await WriteEffect(consumer, e, sp, ct);
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });

        await ProduceAsync(kafka, topic, le, envelope);
        await consumer.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await consumer.StopAsync(CancellationToken.None);

        consumer.Invocations[envelope.EventId].ShouldBe(1); // never retried
        kafka.CommittedOffset(consumer.GroupId, topic).ShouldBeLessThan(0); // nothing committed
        (await EffectsAsync(consumer.GroupId, envelope.EventId)).ShouldBe(0); // effect rolled back
        (await CountInboxRowsAsync(db, consumer.GroupId, envelope.EventId)).ShouldBe(0);
        kafka.ReadAll(DlqHeaders.TopicFor(topic), expected: 1, TimeSpan.FromSeconds(2)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "conventions-events §7: a restart between the effect's commit and the offset commit neither loses nor double-applies any event")]
    public async Task RestartMidStream_NoLossNoDoubleApply()
    {
        var topic = NewTopic();
        await kafka.CreateTopicWithDlqAsync(topic);
        var events = new List<EventEnvelope>();
        for (var i = 0; i < 5; i++)
        {
            var le = LegalEntity($"LE-{i}");
            var e = Envelope(le.id);
            events.Add(e);
            await ProduceAsync(kafka, topic, le, e);
        }

        // First run: crashes after the 3rd record's effect is committed, before its offset is.
        TestLegalEntityConsumer first = null!;
        first = Consumer(Runtime(), topic, "restart-test", (_, e, sp, ct) => WriteEffect(first, e, sp, ct), hangAfterProcessed: 3);
        await first.StartAsync(TestContext.Current.CancellationToken);
        await first.Hanging.Task.WaitAsync(Wait, TestContext.Current.CancellationToken);
        await first.StopAsync(CancellationToken.None);
        kafka.CommittedOffset(first.GroupId, topic).ShouldBe(2);

        // Second run, same group: the 3rd record is redelivered and skipped by the inbox.
        TestLegalEntityConsumer second = null!;
        second = Consumer(Runtime(), topic, "restart-test", (_, e, sp, ct) => WriteEffect(second, e, sp, ct));
        await second.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(kafka.CommittedOffset(second.GroupId, topic) == 5), "all offsets committed");
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
        }

        foreach (var e in events)
        {
            (await EffectsAsync(first.GroupId, e.EventId)).ShouldBe(1, $"event {e.EventId}");
        }

        second.Invocations.Keys.ShouldBe([events[3].EventId, events[4].EventId], ignoreOrder: true);
    }

    [Fact(DisplayName = "make dlq-replay: re-injects the DLQ records into the original topic byte for byte, without nexus-dlq-* headers, once")]
    public async Task DlqReplay_ReinjectsOnce()
    {
        var topic = NewTopic();
        var dlqTopic = DlqHeaders.TopicFor(topic);
        await kafka.CreateTopicWithDlqAsync(topic);
        var le = LegalEntity();
        var envelope = Envelope(le.id);
        var bytes = Encoding.UTF8.GetBytes("raw-bytes-as-they-were");
        var dlqHeaders = DlqHeaders.Build(Headers(envelope), new InvalidOperationException("boom"), "operations.x", DateTimeOffset.UtcNow);
        await ProduceRawAsync(kafka, dlqTopic, le.id.ToString("D"), bytes, dlqHeaders);

        var replayer = new DlqReplayer(kafka.BootstrapServers);
        var plan = replayer.Plan(dlqTopic);
        plan.TargetTopic.ShouldBe(topic);
        plan.Pending.ShouldBe(1);

        (await replayer.ReplayAsync(dlqTopic, TestContext.Current.CancellationToken)).ShouldBe(1);

        var replayed = kafka.ReadAll(topic, expected: 2, TimeSpan.FromSeconds(3));
        replayed.Count.ShouldBe(1);
        replayed[0].Message.Key.ShouldBe(le.id.ToString("D"));
        replayed[0].Message.Value.ShouldBe(bytes);
        Header(replayed[0].Message.Headers, EventHeaderNames.EventId).ShouldBe(envelope.EventId.ToString("D"));
        replayed[0].Message.Headers.Any(h => h.Key.StartsWith("nexus-dlq-", StringComparison.Ordinal)).ShouldBeFalse();

        replayer.Plan(dlqTopic).Pending.ShouldBe(0);
        (await replayer.ReplayAsync(dlqTopic, TestContext.Current.CancellationToken)).ShouldBe(0);
        Should.Throw<ArgumentException>(() => DlqReplayer.OriginalTopic(topic));
    }
}

/// <summary>Counts DLQ publish failures of the wrapped publisher.</summary>
internal sealed class CountingDlqPublisher(IDlqPublisher inner) : IDlqPublisher
{
    private int _failures;

    public int Failures => Volatile.Read(ref _failures);

    public async Task PublishAsync(
        string topic, string? key, byte[]? rawValue, Headers? originalHeaders, Exception failure, string consumerGroup, CancellationToken ct = default)
    {
        try
        {
            await inner.PublishAsync(topic, key, rawValue, originalHeaders, failure, consumerGroup, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref _failures);
            throw;
        }
    }
}

/// <summary>A DLQ that is down until <see cref="Up"/> is set, then forwards to the real one.</summary>
internal sealed class ToggleDlqPublisher(IDlqPublisher inner) : IDlqPublisher
{
    private int _publishes;
    private volatile bool _up;

    public bool Up
    {
        get => _up;
        set => _up = value;
    }

    public int Publishes => Volatile.Read(ref _publishes);

    public Task PublishAsync(
        string topic, string? key, byte[]? rawValue, Headers? originalHeaders, Exception failure, string consumerGroup, CancellationToken ct = default)
    {
        if (!Up)
        {
            return Task.FromException(new KafkaException(new Error(ErrorCode.Local_MsgTimedOut, "DLQ down (test)")));
        }

        Interlocked.Increment(ref _publishes);
        return inner.PublishAsync(topic, key, rawValue, originalHeaders, failure, consumerGroup, ct);
    }
}

internal sealed class AlwaysFailingDlqPublisher : IDlqPublisher
{
    public Task PublishAsync(
        string topic, string? key, byte[]? rawValue, Headers? originalHeaders, Exception failure, string consumerGroup, CancellationToken ct = default) =>
        Task.FromException(new KafkaException(new Error(ErrorCode.Local_MsgTimedOut, "DLQ unreachable (test)")));
}

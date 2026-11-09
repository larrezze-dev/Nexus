using System.Diagnostics;
using System.Net;
using System.Text;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Messaging.Dlq;
using Nexus.BuildingBlocks.Messaging.Health;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Runtime;
using Nexus.Projections.Tests.Runtime.Infrastructure;
using Shouldly;

namespace Nexus.Projections.Tests.Runtime;

/// <summary>
/// The worker loop of one projection against real Kafka + Schema Registry + ClickHouse, with a test
/// projection (<see cref="TestProjection"/>) writing to a per-test ReplacingMergeTree table.
/// </summary>
[Collection(RuntimeTestGroup.Name)]
public sealed class ProjectionConsumerTests(RuntimeEnvironment env)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "LOG-R45: an event produced to Kafka is visible in ClickHouse in < 5 s and the lag histogram records it")]
    public async Task Event_visible_within_five_seconds_and_lag_recorded()
    {
        var (p, topic) = await SetupAsync("lag");
        await using var h = RuntimeHarness.Create(env, [p], c => c[$"{ProjectionOptions.SectionName}:FlushInterval"] = "00:00:01");
        h.Start(p);
        await TestEvents.ProduceAsync(env.Kafka, topic, "warm-up");
        (await Eventually.Until(async () => await Count(p) == 1, Wait)).ShouldBeTrue("warm-up row");

        var watch = Stopwatch.StartNew();
        await TestEvents.ProduceAsync(env.Kafka, topic, "measured");
        var visible = await Eventually.Until(async () => await Count(p) == 2, TimeSpan.FromSeconds(5));
        watch.Stop();

        visible.ShouldBeTrue($"the row must be visible within 5 s (took {watch.Elapsed})");
        var lags = h.Values(ProjectionMetrics.LagName, p.Name).ToList();
        lags.Count.ShouldBe(2);
        lags.ShouldAllBe(l => l >= 0 && l < 5);
        h.Sum(ProjectionMetrics.RowsTotalName, p.Name).ShouldBe(2);
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBe(2);
    }

    [Fact(DisplayName = "Projection idempotency: the same event processed twice is one row with FINAL (and after OPTIMIZE FINAL)")]
    public async Task Same_event_twice_is_one_row()
    {
        var (p, topic) = await SetupAsync("dup");
        var envelope = TestEvents.Envelope(Guid.CreateVersion7(), version: 7);
        await TestEvents.ProduceAsync(env.Kafka, topic, "same", envelope: envelope);
        await TestEvents.ProduceAsync(env.Kafka, topic, "same", envelope: envelope);
        await using var h = RuntimeHarness.Create(env, [p]);
        h.Start(p);

        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 2, Wait)).ShouldBeTrue("both records committed");

        p.MapCalls.ShouldBe(2);
        (await Count(p)).ShouldBe(1);
        await env.ClickHouse.ExecuteAsync($"OPTIMIZE TABLE {p.TableName} FINAL");
        (await env.ClickHouse.CountAsync($"SELECT count() FROM {p.TableName}")).ShouldBe(1);
    }

    [Fact(DisplayName = "Projection batching: a batch is flushed when it reaches MaxBatchRows; offsets cover only inserted rows; cancellation commits nothing")]
    public async Task Flush_by_size_and_cancellation_never_commits()
    {
        var (p, topic) = await SetupAsync("size");
        for (var i = 0; i < 12; i++)
        {
            await TestEvents.ProduceAsync(env.Kafka, topic, $"n{i}");
        }

        var h = RuntimeHarness.Create(env, [p], c =>
        {
            c[$"{ProjectionOptions.SectionName}:MaxBatchRows"] = "5";
            c[$"{ProjectionOptions.SectionName}:FlushInterval"] = "00:10:00";
        });
        await using (h)
        {
            h.Start(p);
            (await Eventually.Until(async () => await RawCount(p) == 10, Wait)).ShouldBeTrue("two full batches");
            (await Eventually.Until(() => p.MapCalls == 12, Wait)).ShouldBeTrue("the last two records are buffered");
            await Task.Delay(1500, Ct);

            (await RawCount(p)).ShouldBe(10, "the partial batch (2 rows) waits for its 10 min interval");
            h.Values(ProjectionMetrics.BatchSizeName, p.Name).ShouldBe([5d, 5d]);
            (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBe(10);
        }

        // Disposing the harness cancelled the loop with 2 records buffered: nothing more was committed.
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBe(10);
        (await RawCount(p)).ShouldBe(10);

        // Restart: the 2 uncommitted records are redelivered and projected.
        await using var again = RuntimeHarness.Create(env, [p]);
        again.Start(p);
        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 12, Wait)).ShouldBeTrue();
        (await Count(p)).ShouldBe(12);
    }

    [Fact(DisplayName = "Projection batching: a partial batch is flushed by time (FlushInterval) in one insert")]
    public async Task Flush_by_time()
    {
        var (p, topic) = await SetupAsync("time");
        for (var i = 0; i < 3; i++)
        {
            await TestEvents.ProduceAsync(env.Kafka, topic, $"n{i}");
        }

        await using var h = RuntimeHarness.Create(env, [p], c => c[$"{ProjectionOptions.SectionName}:FlushInterval"] = "00:00:01");
        h.Start(p);

        (await Eventually.Until(async () => await Count(p) == 3, Wait)).ShouldBeTrue();
        h.Values(ProjectionMetrics.BatchSizeName, p.Name).ShouldBe([3d]);
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBe(3);
    }

    [Fact(DisplayName = "Projection transient failure: ClickHouse insert failing → no commit, no DLQ, Unhealthy; recovery inserts the rows")]
    public async Task ClickHouse_failure_never_commits_nor_dead_letters()
    {
        var (p, topic) = await SetupAsync("chdown", createTable: false); // the insert fails: table missing
        for (var i = 0; i < 3; i++)
        {
            await TestEvents.ProduceAsync(env.Kafka, topic, $"n{i}");
        }

        await using var h = RuntimeHarness.Create(env, [p]);
        h.Start(p);

        (await Eventually.Until(() => h.State(p.GroupId) == ConsumerLoopState.DependencyUnavailable, Wait)).ShouldBeTrue("the loop reports the outage");
        var report = await h.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        report.Status.ShouldBe(HealthStatus.Unhealthy);
        report.Entries["kafka-consumers"].Data[$"{p.GroupId}.lag"].ShouldBe(3L, "the lag probe expands the subscription");
        await Task.Delay(1500, Ct); // a few more failed attempts
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindInsertUnavailable).ShouldBeGreaterThanOrEqualTo(2);
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBeNull();
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, TimeSpan.FromSeconds(1)).ShouldBeEmpty();
        h.Sum(ProjectionMetrics.DlqTotalName, p.Name).ShouldBe(0);

        await env.ClickHouse.ExecuteAsync(p.CreateTableSql); // ClickHouse "comes back"

        (await Eventually.Until(async () => await Count(p) == 3, Wait)).ShouldBeTrue("the kept batch is inserted");
        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 3, Wait)).ShouldBeTrue();
        h.State(p.GroupId).ShouldBe(ConsumerLoopState.Running);
        p.MapCalls.ShouldBe(3, "the batch was kept, not re-read");
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, TimeSpan.FromSeconds(1)).ShouldBeEmpty();
    }

    [Fact(DisplayName = "Projection transient failure: Schema Registry unreachable → paused, Unhealthy, no DLQ, no commit; projected once it answers")]
    public async Task Schema_registry_outage_is_transient()
    {
        var (p, topic) = await SetupAsync("srdown");
        await TestEvents.ProduceAsync(env.Kafka, topic, "after-outage"); // registers the schema while the registry is up

        await using var h = RuntimeHarness.Create(env, [p]);
        await env.Kafka.PauseRegistryAsync();
        try
        {
            h.Start(p); // a fresh registry client: the schema is not cached
            (await Eventually.Until(() => h.State(p.GroupId) == ConsumerLoopState.DependencyUnavailable, Wait)).ShouldBeTrue("the loop reports the outage");
            h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindSchemaRegistry).ShouldBeGreaterThanOrEqualTo(1);
            h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindRecord).ShouldBe(0, "a transient failure spends no record attempt");
        }
        finally
        {
            await env.Kafka.UnpauseRegistryAsync();
        }

        (await Eventually.Until(async () => await Count(p) == 1, Wait)).ShouldBeTrue();
        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 1, Wait)).ShouldBeTrue();
        h.Sum(ProjectionMetrics.DlqTotalName, p.Name).ShouldBe(0);
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, TimeSpan.FromSeconds(1)).ShouldBeEmpty();
        h.State(p.GroupId).ShouldBe(ConsumerLoopState.Running);
    }

    [Fact(DisplayName = "Projection DLQ: poison records (garbage bytes, unknown schema id, missing header, Map throwing) go to {topic}.DLQ with nexus-dlq-* headers; later records still projected")]
    public async Task Poison_records_go_to_dlq()
    {
        var (p, topic) = await SetupAsync("poison");
        var garbage = Encoding.UTF8.GetBytes("this is not avro");
        var unknownSchema = new byte[] { 0, 0, 0x0F, 0x42, 0x3F, 2, (byte)'x' }; // magic byte + schema id 999999
        var valid = await env.Kafka.SerializeAsync(topic, TestEvents.Payload("ok-but-headerless"));
        var good = TestEvents.Envelope(Guid.CreateVersion7());
        await env.Kafka.ProduceRawAsync(topic, "k1", garbage, TestEvents.Headers(TestEvents.Envelope(Guid.CreateVersion7())));
        await env.Kafka.ProduceRawAsync(topic, "k2", unknownSchema, TestEvents.Headers(TestEvents.Envelope(Guid.CreateVersion7())));
        await env.Kafka.ProduceRawAsync(topic, "k3", valid, TestEvents.Headers(TestEvents.Envelope(Guid.CreateVersion7()), EventHeaderNames.EventId));
        await TestEvents.ProduceAsync(env.Kafka, topic, "explode");
        await TestEvents.ProduceAsync(env.Kafka, topic, "fine", envelope: good);

        await using var h = RuntimeHarness.Create(env, [p]);
        h.Start(p);

        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 5, Wait)).ShouldBeTrue("every record accounted for");
        (await env.ClickHouse.RowsAsync($"SELECT name FROM {p.TableName} FINAL")).ShouldBe(["fine"]);

        var dlq = env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 4, Wait);
        dlq.Count.ShouldBe(4);
        dlq.Take(3).Select(r => r.Message.Key).ShouldBe(["k1", "k2", "k3"]);
        dlq[0].Message.Value.ShouldBe(garbage, "the ORIGINAL bytes are forwarded");
        dlq[1].Message.Value.ShouldBe(unknownSchema);
        foreach (var record in dlq)
        {
            Header(record, DlqHeaders.Consumer).ShouldBe(p.GroupId);
            Header(record, DlqHeaders.Error).ShouldNotBeNullOrWhiteSpace();
            Header(record, DlqHeaders.FailedAt).ShouldNotBeNullOrWhiteSpace();
        }

        Header(dlq[2], DlqHeaders.Error)!.ShouldContain(EventHeaderNames.EventId);
        Header(dlq[3], DlqHeaders.Error)!.ShouldContain("explode");
        h.Sum(ProjectionMetrics.DlqTotalName, p.Name).ShouldBe(4);
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindRecord).ShouldBe(16, "4 attempts per poison record");
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindSchemaRegistry).ShouldBe(0, "schema not found is the record's fault, not an outage");
    }

    [Fact(DisplayName = "Projection subscription: the event_log pattern never consumes nexus.x.y.z.DLQ and picks up topics created later")]
    public async Task Pattern_excludes_dlq_and_picks_up_new_topics()
    {
        var prefix = $"nexus.rt{Guid.NewGuid():N}"[..20];
        var created = $"{prefix}.thing.created";
        var updated = $"{prefix}.thing.updated";
        var name = TestProjection.UniqueName("pattern");
        var p = new TestProjection(name, ProjectionSubscription.ForPattern(@"^nexus\.[a-z0-9-]+\.[a-z0-9-]+\.[a-z0-9-]+$"), ownTopicPrefix: prefix + ".");
        await env.ClickHouse.ExecuteAsync(p.CreateTableSql);
        await env.Kafka.CreateTopicsAsync(created, DlqHeaders.TopicFor(created));
        await TestEvents.ProduceAsync(env.Kafka, created, "main");
        await TestEvents.ProduceAsync(env.Kafka, DlqHeaders.TopicFor(created), "from-dlq");

        await using var h = RuntimeHarness.Create(env, [p]);
        h.Start(p);
        (await Eventually.Until(async () => await Count(p) == 1, Wait)).ShouldBeTrue();

        await env.Kafka.CreateTopicsAsync(updated);
        await TestEvents.ProduceAsync(env.Kafka, updated, "late");
        (await Eventually.Until(async () => await Count(p) == 2, Wait)).ShouldBeTrue("the new topic is picked up by the regex");

        (await env.ClickHouse.RowsAsync($"SELECT name FROM {p.TableName} FINAL ORDER BY name")).ShouldBe(["late", "main"]);
        (await env.Kafka.CommittedAsync(p.GroupId, DlqHeaders.TopicFor(created))).ShouldBeNull("the DLQ topic is not even subscribed");
    }

    [Fact(DisplayName = "Projection rebalance: records buffered by a consumer that loses its partition are dropped uncommitted and re-read by the new owner")]
    public async Task Rebalance_drops_uncommitted_buffer()
    {
        var (p, topic) = await SetupAsync("rebal");
        for (var i = 0; i < 3; i++)
        {
            await TestEvents.ProduceAsync(env.Kafka, topic, $"n{i}");
        }

        var slow = RuntimeHarness.Create(env, [p], c => c[$"{ProjectionOptions.SectionName}:FlushInterval"] = "00:10:00");
        await using (slow)
        {
            slow.Start(p);
            (await Eventually.Until(() => p.MapCalls == 3, Wait)).ShouldBeTrue("the slow consumer buffered the 3 records");
            (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBeNull();

            await using var fast = RuntimeHarness.Create(env, [p]);
            fast.Start(p); // joins the group: rebalance, the slow member's buffer is dropped
            await Task.Delay(3000, Ct);
            await slow.StopAllAsync(); // the slow member leaves (Close: revocation, nothing committed)
            // Per-harness metric (not a raw row count, which background merges can shrink): the slow
            // consumer never inserted anything.
            slow.Sum(ProjectionMetrics.RowsTotalName, p.Name).ShouldBe(0);
        }

        // The slow member left (Close) without committing. Whoever owns the partition re-reads it.
        await using var last = RuntimeHarness.Create(env, [p]);
        last.Start(p);
        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 3, Wait)).ShouldBeTrue();
        (await Count(p)).ShouldBe(3);
        p.MapCalls.ShouldBeGreaterThanOrEqualTo(6, "the dropped records were re-read by the partition's next owner");
    }

    [Fact(DisplayName = "Projection critical path: ClickHouse down while a second member joins → partitions revoked during the wait; after recovery N rows, N committed, no gap, no duplicate")]
    public async Task Outage_with_rebalance_during_wait_loses_nothing()
    {
        const int perPartition = 5;
        var name = TestProjection.UniqueName("outrebal");
        var topic = $"test.{name}";
        var p = new TestProjection(name, ProjectionSubscription.ForTopics(topic));
        await env.Kafka.CreateTopicsAsync(2, topic, DlqHeaders.TopicFor(topic)); // table NOT created: inserts fail
        var expected = new List<Guid>();
        for (var partition = 0; partition < 2; partition++)
        {
            for (var i = 0; i < perPartition; i++)
            {
                var envelope = TestEvents.Envelope(Guid.CreateVersion7());
                expected.Add(envelope.EventId);
                var bytes = await env.Kafka.SerializeAsync(topic, TestEvents.Payload($"p{partition}-{i}"));
                await env.Kafka.ProduceRawAsync(new TopicPartition(topic, partition), envelope.AggregateId.ToString(), bytes, TestEvents.Headers(envelope));
            }
        }

        await using var first = RuntimeHarness.Create(env, [p]);
        first.Start(p);
        (await Eventually.Until(() => first.State(p.GroupId) == ConsumerLoopState.DependencyUnavailable, Wait)).ShouldBeTrue("first member blocked on the insert");
        (await Eventually.Until(() => p.MapCalls == 2 * perPartition, Wait)).ShouldBeTrue("first member buffered both partitions");

        // A second member joins WHILE the first one waits (paused, polling to stay in the group): the
        // rebalance revokes its partitions inside that keep-alive poll and drops its buffer.
        await using var second = RuntimeHarness.Create(env, [p]);
        second.Start(p);
        (await Eventually.Until(() => p.MapCalls > 2 * perPartition, Wait)).ShouldBeTrue("revoked records are re-read by their new owner");
        (await env.Kafka.CommittedAsync(p.GroupId, topic, 2)).ShouldBe([-1L, -1L], "nothing committed during the outage");

        await env.ClickHouse.ExecuteAsync(p.CreateTableSql); // ClickHouse back

        (await Eventually.Until(async () => (await env.Kafka.CommittedAsync(p.GroupId, topic, 2)).SequenceEqual([perPartition, perPartition]), Wait))
            .ShouldBeTrue("every partition committed to its end");
        var ids = await env.ClickHouse.RowsAsync($"SELECT _event_id FROM {p.TableName} FINAL");
        ids.Count.ShouldBe(2 * perPartition, "no gap and no duplicate beyond the ReplacingMergeTree collapse");
        ids.Order(StringComparer.Ordinal).ShouldBe(expected.Select(g => g.ToString()).Order(StringComparer.Ordinal));
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, TimeSpan.FromSeconds(1)).ShouldBeEmpty();
        (await Eventually.Until(() => first.State(p.GroupId) is ConsumerLoopState.Running or ConsumerLoopState.Stopped
            && second.State(p.GroupId) == ConsumerLoopState.Running, Wait)).ShouldBeTrue("states restored after recovery");
    }

    [Fact(DisplayName = "Projection critical path: both members blocked by a ClickHouse outage, one LEAVES during the wait → the survivor gets new partitions while paused (strays rewound); after recovery N rows, N committed, no gap")]
    public async Task Member_leaving_during_outage_loses_nothing()
    {
        const int perPartition = 5;
        var name = TestProjection.UniqueName("outleave");
        var topic = $"test.{name}";
        var p = new TestProjection(name, ProjectionSubscription.ForTopics(topic));
        await env.Kafka.CreateTopicsAsync(2, topic, DlqHeaders.TopicFor(topic)); // table NOT created: inserts fail
        var expected = new List<Guid>();
        for (var partition = 0; partition < 2; partition++)
        {
            for (var i = 0; i < perPartition; i++)
            {
                var envelope = TestEvents.Envelope(Guid.CreateVersion7());
                expected.Add(envelope.EventId);
                var bytes = await env.Kafka.SerializeAsync(topic, TestEvents.Payload($"p{partition}-{i}"));
                await env.Kafka.ProduceRawAsync(new TopicPartition(topic, partition), envelope.AggregateId.ToString(), bytes, TestEvents.Headers(envelope));
            }
        }

        // Long waits: the survivor must spend the rebalance INSIDE its keep-alive wait, where the partition
        // it inherits arrives un-paused and its records show up as strays.
        static void SlowBackoff(Dictionary<string, string?> c)
        {
            c[$"{ProjectionOptions.SectionName}:TransientInitialBackoff"] = "00:00:04";
            c[$"{ProjectionOptions.SectionName}:TransientMaxBackoff"] = "00:00:04";
        }

        // One projection INSTANCE per member (same name, so same group and table): separate Map counters.
        var survivorP = new TestProjection(name, ProjectionSubscription.ForTopics(topic));
        var leaverP = new TestProjection(name, ProjectionSubscription.ForTopics(topic));
        await using var survivor = RuntimeHarness.Create(env, [survivorP], SlowBackoff);
        var leaver = RuntimeHarness.Create(env, [leaverP], SlowBackoff);
        await using (leaver)
        {
            // The leaver starts alone and takes BOTH partitions; the survivor then joins and gets ONE. So the
            // partition the survivor inherits later is new to its client: librdkafka keeps a paused partition
            // paused across a re-assignment only for partitions the client already owned.
            leaver.Start(leaverP);
            (await Eventually.Until(() => leaver.State(p.GroupId) == ConsumerLoopState.DependencyUnavailable
                && leaverP.MapCalls >= 2 * perPartition, Wait)).ShouldBeTrue("the leaver holds both partitions, blocked on the insert");
            survivor.Start(survivorP);
            (await Eventually.Until(() => survivor.State(p.GroupId) == ConsumerLoopState.DependencyUnavailable
                && survivorP.MapCalls >= perPartition, Wait)).ShouldBeTrue("the survivor holds one partition, blocked on the insert");
            survivorP.MapCalls.ShouldBeLessThan(2 * perPartition, "the survivor must not have owned the leaver's partition yet");
            await leaver.StopAllAsync(); // leaves the group: its partition goes to the survivor
        }

        await Task.Delay(TimeSpan.FromSeconds(6), Ct); // the survivor's wait spans the rebalance
        (await env.Kafka.CommittedAsync(p.GroupId, topic, 2)).ShouldBe([-1L, -1L], "nothing committed during the outage");

        await env.ClickHouse.ExecuteAsync(p.CreateTableSql); // ClickHouse back

        (await Eventually.Until(async () => (await env.Kafka.CommittedAsync(p.GroupId, topic, 2)).SequenceEqual([perPartition, perPartition]), Wait))
            .ShouldBeTrue("both partitions committed to their end by the survivor");
        var ids = await env.ClickHouse.RowsAsync($"SELECT _event_id FROM {p.TableName} FINAL");
        ids.Order(StringComparer.Ordinal).ShouldBe(expected.Select(g => g.ToString()).Order(StringComparer.Ordinal), "no gap, no duplicate");
        survivor.State(p.GroupId).ShouldBe(ConsumerLoopState.Running);
        survivorP.MapCalls.ShouldBeGreaterThanOrEqualTo(2 * perPartition, "the survivor projected the inherited partition");
    }

    [Fact(DisplayName = "Projection DLQ unavailable: DLQ publish failing → paused, Unhealthy, no commit; once the DLQ answers the record is dead-lettered and committed")]
    public async Task Dlq_unavailable_pauses_then_recovers()
    {
        var name = TestProjection.UniqueName("dlqdown");
        var topic = $"test.{name}";
        var p = new TestProjection(name, ProjectionSubscription.ForTopics(topic));
        await env.Kafka.CreateTopicsAsync(topic); // {topic}.DLQ does NOT exist: the publish times out
        await env.ClickHouse.ExecuteAsync(p.CreateTableSql);
        await TestEvents.ProduceAsync(env.Kafka, topic, "explode");
        await TestEvents.ProduceAsync(env.Kafka, topic, "after");

        await using var h = RuntimeHarness.Create(env, [p], c => c[$"{Nexus.BuildingBlocks.Messaging.NexusKafkaOptions.SectionName}:DlqMessageTimeout"] = "00:00:02");
        h.Start(p);

        (await Eventually.Until(() => h.State(p.GroupId) == ConsumerLoopState.DlqUnavailable, Wait)).ShouldBeTrue();
        (await h.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct)).Status.ShouldBe(HealthStatus.Unhealthy);
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBeNull();
        (await Count(p)).ShouldBe(0, "the next record waits: the poison one is never skipped");

        await env.Kafka.CreateTopicsAsync(DlqHeaders.TopicFor(topic)); // the DLQ comes back

        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 2, Wait)).ShouldBeTrue();
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, Wait).Count.ShouldBe(1);
        (await env.ClickHouse.RowsAsync($"SELECT name FROM {p.TableName} FINAL")).ShouldBe(["after"]);
        h.State(p.GroupId).ShouldBe(ConsumerLoopState.Running);
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindDlq).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact(DisplayName = "Projection insert rejected (D1): a batch ClickHouse refuses blocks the projection — no commit, no DLQ, kind=insert_rejected, no row values in the reported error")]
    public async Task Insert_rejected_blocks_without_dlq()
    {
        var (p, topic) = await SetupAsync("rejected");
        await TestEvents.ProduceAsync(env.Kafka, topic, "bad-type");

        await using var h = RuntimeHarness.Create(env, [p]);
        h.Start(p);

        (await Eventually.Until(() => h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindInsertRejected) >= 2, Wait))
            .ShouldBeTrue("the rejected insert is retried (capped back-off) and counted as insert_rejected");
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindInsertUnavailable).ShouldBe(0);
        h.State(p.GroupId).ShouldBe(ConsumerLoopState.DependencyUnavailable);
        h.Error(p.GroupId)!.ShouldContain("rejected the batch");
        h.Error(p.GroupId)!.ShouldNotContain("not-a-number");
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBeNull();
        (await RawCount(p)).ShouldBe(0);
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, TimeSpan.FromSeconds(1)).ShouldBeEmpty();
        h.Sum(ProjectionMetrics.DlqTotalName, p.Name).ShouldBe(0);
    }

    [Theory(DisplayName = "Projection D3: Schema Registry refusing our credentials (401/403/407) is infrastructure — paused, Unhealthy, never DLQ; projected once it accepts")]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    public async Task Schema_registry_auth_refusal_is_infrastructure(HttpStatusCode status)
    {
        var (p, topic) = await SetupAsync("srauth");
        await TestEvents.ProduceAsync(env.Kafka, topic, "after-auth");
        SwitchableSchemaRegistry? control = null;

        await using var h = RuntimeHarness.Create(env, [p], overrides: services => services.AddSingleton<ISchemaRegistryClient>(_ =>
        {
            var (client, sw) = SwitchableSchemaRegistry.Create(new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = env.Kafka.SchemaRegistryUrl }));
            sw.FailWith = status;
            control = sw;
            return client;
        }));
        h.Start(p);

        (await Eventually.Until(() => h.State(p.GroupId) == ConsumerLoopState.DependencyUnavailable, Wait)).ShouldBeTrue();
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindSchemaRegistry).ShouldBeGreaterThanOrEqualTo(1);
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindRecord).ShouldBe(0);
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBeNull();

        control!.FailWith = null; // credentials accepted again

        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 1, Wait)).ShouldBeTrue();
        (await Count(p)).ShouldBe(1);
        h.Sum(ProjectionMetrics.DlqTotalName, p.Name).ShouldBe(0);
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 1, TimeSpan.FromSeconds(1)).ShouldBeEmpty();
        h.State(p.GroupId).ShouldBe(ConsumerLoopState.Running);
    }

    [Fact(DisplayName = "rebuild (D2): poison records are counted and skipped, never re-published to the DLQ; exit 0 with the same rows")]
    public async Task Rebuild_skips_poison_without_dlq()
    {
        var (p, topic) = await SetupAsync("rbpoison");
        await TestEvents.ProduceAsync(env.Kafka, topic, "explode");
        await TestEvents.ProduceAsync(env.Kafka, topic, "kept");
        await using (var worker = RuntimeHarness.Create(env, [p]))
        {
            worker.Start(p);
            (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 2, Wait)).ShouldBeTrue();
        }

        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 2, TimeSpan.FromSeconds(3)).Count.ShouldBe(1, "the worker dead-lettered it once");

        await using var rebuild = RuntimeHarness.Create(env, [p]);
        var summary = await rebuild.Rebuilder.RunAsync(p.Name, Ct);

        summary.PoisonRecordsSkipped.ShouldBe(1);
        summary.TotalRows.ShouldBe(1);
        env.Kafka.ReadAll(DlqHeaders.TopicFor(topic), 2, TimeSpan.FromSeconds(3)).Count.ShouldBe(1, "rebuild never re-publishes to the DLQ");
        rebuild.Sum(ProjectionMetrics.DlqTotalName, p.Name).ShouldBe(0);
        (await env.ClickHouse.RowsAsync($"SELECT name FROM {p.TableName} FINAL")).ShouldBe(["kept"]);
        (await env.Kafka.CommittedAsync(p.GroupId, topic)).ShouldBe(2);
    }

    private async Task<(TestProjection Projection, string Topic)> SetupAsync(string prefix, bool createTable = true)
    {
        var name = TestProjection.UniqueName(prefix);
        var topic = $"test.{name}";
        var projection = new TestProjection(name, ProjectionSubscription.ForTopics(topic));
        await env.Kafka.CreateTopicsAsync(topic, DlqHeaders.TopicFor(topic));
        if (createTable)
        {
            await env.ClickHouse.ExecuteAsync(projection.CreateTableSql);
        }

        return (projection, topic);
    }

    private Task<long> Count(TestProjection p) => env.ClickHouse.CountAsync($"SELECT count() FROM {p.TableName} FINAL");

    private Task<long> RawCount(TestProjection p) => env.ClickHouse.CountAsync($"SELECT count() FROM {p.TableName}");

    private static string? Header(ConsumeResult<string?, byte[]?> record, string name) =>
        record.Message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
}

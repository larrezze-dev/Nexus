using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
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
        h.Sum(ProjectionMetrics.ErrorsTotalName, p.Name, ProjectionMetrics.ErrorKindInsert).ShouldBeGreaterThanOrEqualTo(2);
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
        }

        // The slow member left (Close) without committing. Whoever owns the partition re-reads it.
        await using var last = RuntimeHarness.Create(env, [p]);
        last.Start(p);
        (await Eventually.Until(async () => await env.Kafka.CommittedAsync(p.GroupId, topic) == 3, Wait)).ShouldBeTrue();
        (await Count(p)).ShouldBe(3);
        (await RawCount(p)).ShouldBe(3, "the slow consumer never inserted (nor committed) its dropped buffer");
        p.MapCalls.ShouldBeGreaterThanOrEqualTo(6, "the dropped records were re-read by the partition's next owner");
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

using System.Diagnostics;
using Avro.Generic;
using Confluent.SchemaRegistry;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Publishing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>
/// Step 0.7 "Tests (B)": the whole relay (RelayWorker + PostgresRelayLeadership as svc_relay +
/// KafkaEventPublisher + SchemaRegistryPayloadEncoder) against real PostgreSQL, Kafka and Schema
/// Registry containers. Each test uses its own topic and clears outbox.message first.
///
/// The "full headers per §2" scenario of the ficha is already covered end to end (real
/// UnitOfWork + OutboxWriter) by <see cref="KafkaPublishingTests.UnitOfWorkEvent_IsPublishedWithAllHeaders"/>.
/// </summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class RelayEndToEndTests(PostgresFixture db, KafkaFixture kafka)
{
    private static void Log(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);

    private async Task<string> NewProbeTopicAsync()
    {
        await OutboxRows.ClearAsync(db);
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        await kafka.RegisterAsync(topic, ProbeRows.SchemaJson);
        return topic;
    }

    private static DateTimeOffset Start() => new(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);

    [Fact(DisplayName = "EV-R3: 1000 events of 10 interleaved aggregates reach Kafka exactly once and, per key, in strictly increasing aggregate_version")]
    public async Task EvR3_ThousandInterleavedEvents_InOrderPerKey()
    {
        var topic = await NewProbeTopicAsync();
        var aggregates = Enumerable.Range(0, 10).Select(_ => Guid.CreateVersion7()).ToList();
        var rows = ProbeRows.Interleaved(1000, aggregates, seed: 20261107, Start());

        // Half is waiting before the relay starts (drained in full batches), half arrives while it runs.
        await ProbeRows.InsertAsync(db, topic, rows[..500]);
        await using var relay = new RelayInstance(db, kafka, new RelayOptions { PollIntervalMs = 100, LeaderRetrySeconds = 1, ProduceTimeoutMs = 15_000 });
        var stopwatch = Stopwatch.StartNew();
        await relay.StartAsync();
        foreach (var chunk in rows[500..].Chunk(100))
        {
            await ProbeRows.InsertAsync(db, topic, chunk);
        }

        await Poll.UntilAsync(async () => await ProbeRows.CountAsync(db, topic, "PUBLISHED") == 1000, TimeSpan.FromSeconds(180), "all 1000 rows PUBLISHED");
        stopwatch.Stop();

        var seconds = stopwatch.Elapsed.TotalSeconds;
        Log($"EV-R3: 1000 events published in {seconds:0.00} s ({1000 / seconds:0} msg/s, {stopwatch.Elapsed.TotalMilliseconds / 1000:0.00} ms/msg; serial ack per message).");
        if (seconds > 60)
        {
            Log("WARNING: more than 60 s for 1000 events.");
        }

        (await ProbeRows.CountAsync(db, topic, "FAILED")).ShouldBe(0);
        relay.Publisher.Count.ShouldBe(1000, "every row acknowledged exactly once by the relay");

        var events = KafkaRead.ReadToEnd(kafka, topic);
        events.Count.ShouldBe(1000, "exactly once in Kafka: high watermark = 1000");
        events.Select(e => e.EventId).ToHashSet().SetEquals(rows.Select(r => r.Id)).ShouldBeTrue("each outbox row is one Kafka record");

        foreach (var group in events.GroupBy(e => e.Key))
        {
            var versions = group.Select(e => e.AggregateVersion).ToList();
            versions.ShouldBe(Enumerable.Range(1, versions.Count).Select(v => (long)v).ToList(), $"key {group.Key}: strictly increasing, no gaps");
            foreach (var e in group)
            {
                var (_, datum) = AvroJson.DecodeWire(e.Value, ProbeRows.Schema);
                var record = (GenericRecord)datum;
                record["probeId"].ShouldBe(Guid.Parse(e.Key));
                ((Avro.AvroDecimal)record["amount"]).ShouldBe(new Avro.AvroDecimal(e.AggregateVersion + 0.0000m));
            }
        }

        events.Select(e => e.Key).Distinct().Count().ShouldBe(10);
    }

    [Fact(DisplayName = "§4.1 / EV-R3: of two relay instances only the leader publishes; when it crashes mid-batch the passive one takes over in < 10 s with no row skipped or reordered")]
    public async Task TwoInstances_OnlyLeaderPublishes_FailoverUnderTenSeconds()
    {
        var topic = await NewProbeTopicAsync();
        var aggregates = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList();
        var rows = ProbeRows.Interleaved(500, aggregates, seed: 7, Start());
        // Ficha: the passive instance retries every 5 s.
        RelayOptions Options() => new() { PollIntervalMs = 100, LeaderRetrySeconds = 5, ProduceTimeoutMs = 15_000 };

        await using var a = new RelayInstance(db, kafka, Options());
        await a.StartAsync();
        await Poll.UntilAsync(() => a.Leadership.LeaderPid is not null, TimeSpan.FromSeconds(15), "A becomes leader");

        await using var b = new RelayInstance(db, kafka, Options());
        await b.StartAsync();

        // Phase 1: only A publishes.
        await ProbeRows.InsertAsync(db, topic, rows[..100]);
        await Poll.UntilAsync(async () => await ProbeRows.CountAsync(db, topic, "PUBLISHED") == 100, TimeSpan.FromSeconds(60), "phase 1 published");
        a.Publisher.Count.ShouldBe(100);
        b.Publisher.Count.ShouldBe(0, "the passive instance never produces");
        b.Leadership.LeaderPid.ShouldBeNull("B never obtained the lock");

        // Phase 2: A crashes in the middle of a 300-row batch, right after its 50th ack (session
        // terminated by the server: no unlock, no commit). Those 50 rows were sent to Kafka but
        // their PUBLISHED marks are rolled back with the batch.
        const int CrashAfter = 150;
        var crashed = new TaskCompletionSource<DateTime>(TaskCreationOptions.RunContinuationsAsynchronously);
        a.Publisher.AfterPublished = async count =>
        {
            if (count == CrashAfter)
            {
                await a.Leadership.CrashAsync(db);
                crashed.TrySetResult(DateTime.UtcNow);
            }
        };
        await ProbeRows.InsertAsync(db, topic, rows[100..400]);
        var crashAt = await crashed.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);

        await Poll.UntilAsync(() => b.Publisher.FirstPublishSince(crashAt) is not null, TimeSpan.FromSeconds(20), "B takes over");
        var takeover = b.Publisher.FirstPublishSince(crashAt)!.Value - crashAt;
        Log($"Failover: B published its first row {takeover.TotalSeconds:0.00} s after A crashed (LeaderRetrySeconds = 5).");
        takeover.ShouldBeLessThan(TimeSpan.FromSeconds(10));

        // Phase 3: rows created after the crash.
        await ProbeRows.InsertAsync(db, topic, rows[400..]);
        await Poll.UntilAsync(async () => await ProbeRows.CountAsync(db, topic, "PUBLISHED") == 500, TimeSpan.FromSeconds(60), "all 500 rows published");
        (await ProbeRows.CountAsync(db, topic, "FAILED")).ShouldBe(0);

        a.Publisher.Count.ShouldBe(CrashAfter, "the crashed instance produced nothing after the crash");
        b.Publisher.Count.ShouldBe(500 - 100, "B republished A's uncommitted batch from its first row on, then everything else");

        var events = KafkaRead.ReadToEnd(kafka, topic);
        events.Select(e => e.EventId).ToHashSet().SetEquals(rows.Select(r => r.Id)).ShouldBeTrue("no row skipped");
        var duplicates = events.Count - rows.Count;
        Log($"Failover: {events.Count} Kafka records for {rows.Count} rows ({duplicates} re-delivered: A's acknowledged but uncommitted rows, at-least-once).");
        duplicates.ShouldBe(CrashAfter - 100, "exactly the rows A sent in the batch that died with its session");

        KafkaRead.ShouldHaveNoGapsPerKey(
            events,
            rows.GroupBy(r => r.AggregateId).ToDictionary(g => g.Key.ToString("D"), g => g.Max(r => r.Version)));

        // Within each instance's own output the order per key is strictly increasing: the
        // re-delivery is a replay of a prefix, never an inversion.
        var bStart = events.First(e => e.Offset >= CrashAfter).Offset;
        foreach (var group in events.Where(e => e.Offset >= bStart).GroupBy(e => e.Key))
        {
            var versions = group.Select(e => e.AggregateVersion).ToList();
            versions.ShouldBe(versions.Order().ToList(), $"key {group.Key}: B publishes in order");
            versions.Distinct().Count().ShouldBe(versions.Count);
        }
    }

    [Fact(DisplayName = "§4.4/§4.5: Schema Registry down ⇒ rows stay PENDING, only the head row's attempts grow (never FAILED during the outage); registry back ⇒ all published in order")]
    public async Task RegistryDown_RowsStayPending_PublishedWhenBack()
    {
        var topic = await NewProbeTopicAsync();
        var aggregates = Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToList();
        var rows = ProbeRows.Interleaved(6, aggregates, seed: 3, Start());

        var options = new RelayOptions
        {
            PollIntervalMs = 200,
            MaxBackoffSeconds = 2,
            LeaderRetrySeconds = 1,
            MaxAttempts = 10,
            ProduceTimeoutMs = 15_000,
            SchemaRegistryUrl = kafka.SchemaRegistryUrl,
        };
        // The production client configuration, with a shorter request timeout so the test is fast.
        // A fresh client and encoder: nothing is cached for this topic yet.
        var registryConfig = SchemaRegistryPayloadEncoder.CreateClientConfig(options);
        registryConfig.RequestTimeoutMs = 2_000;
        registryConfig.MaxRetries = 1;
        await using var relay = new RelayInstance(db, kafka, options, new CachedSchemaRegistryClient(registryConfig));

        var head = rows[0].Id;
        var observed = new List<int>();
        var outage = Stopwatch.StartNew();
        await kafka.PauseRegistryAsync();
        try
        {
            await ProbeRows.InsertAsync(db, topic, rows);
            await relay.StartAsync();

            await Poll.UntilAsync(
                async () =>
                {
                    var row = await OutboxRows.GetAsync(db, head);
                    row.Status.ShouldBe("PENDING", "never FAILED (nor PUBLISHED) while the registry is down");
                    if (observed.Count == 0 || observed[^1] != row.Attempts)
                    {
                        observed.Add(row.Attempts);
                    }

                    return row.Attempts >= 3;
                },
                TimeSpan.FromSeconds(120),
                "the head row records at least 3 failed attempts");

            observed.ShouldBe(observed.Order().ToList(), "attempts only grow");
            var final = await OutboxRows.GetAsync(db, head);
            final.Attempts.ShouldBeLessThan(options.MaxAttempts);
            final.LastError.ShouldNotBeNull().ShouldContain("unavailable");
            (await ProbeRows.CountAsync(db, topic, "PENDING")).ShouldBe(rows.Count);
            (await ProbeRows.CountAsync(db, topic, "FAILED")).ShouldBe(0);
            foreach (var other in rows.Skip(1))
            {
                (await OutboxRows.GetAsync(db, other.Id)).Attempts.ShouldBe(0, "the batch stops at the first transient failure: later rows are not attempted");
            }

            relay.Publisher.Count.ShouldBe(0);
            Log($"Registry outage: {outage.Elapsed.TotalSeconds:0.0} s, head row attempts observed {string.Join(" -> ", observed)} (MaxAttempts = {options.MaxAttempts}).");
        }
        finally
        {
            await kafka.UnpauseRegistryAsync();
        }

        await Poll.UntilAsync(async () => await ProbeRows.CountAsync(db, topic, "PUBLISHED") == rows.Count, TimeSpan.FromSeconds(60), "published once the registry is back");
        var recovered = outage.Elapsed;
        Log($"Registry back: all {rows.Count} rows published {recovered.TotalSeconds:0.0} s after the outage started.");

        var events = KafkaRead.ReadToEnd(kafka, topic);
        events.Count.ShouldBe(rows.Count);
        foreach (var group in events.GroupBy(e => e.Key))
        {
            var versions = group.Select(e => e.AggregateVersion).ToList();
            versions.ShouldBe(Enumerable.Range(1, versions.Count).Select(v => (long)v).ToList(), $"key {group.Key}: in order");
        }

        // Documented behaviour: once a (subject, version) has been resolved, the encoder no longer
        // needs the registry for it. A second outage is invisible for rows of the same topic and
        // schema version (relevant for the ficha's manual check 3: it only shows attempts rising if
        // the relay has not yet published that event type since it started).
        await kafka.PauseRegistryAsync();
        try
        {
            var next = new ProbeRows.Row(Guid.CreateVersion7(), aggregates[0], rows.Where(r => r.AggregateId == aggregates[0]).Max(r => r.Version) + 1, DateTimeOffset.UtcNow, "");
            next = next with { Payload = ProbeRows.Payload(next.AggregateId, next.Version) };
            await ProbeRows.InsertAsync(db, topic, [next]);
            await Poll.UntilAsync(async () => (await OutboxRows.GetAsync(db, next.Id)).Status == "PUBLISHED", TimeSpan.FromSeconds(15), "a cached schema needs no registry");
            (await OutboxRows.GetAsync(db, next.Id)).Attempts.ShouldBe(0);
        }
        finally
        {
            await kafka.UnpauseRegistryAsync();
        }
    }
}

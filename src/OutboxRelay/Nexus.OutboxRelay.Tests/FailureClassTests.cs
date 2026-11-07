using System.Diagnostics;
using System.Diagnostics.Metrics;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Publishing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Shouldly;

namespace Nexus.OutboxRelay.Tests;

/// <summary>
/// The three failure classes of the relay (decision of Luciano, conventions-events.md §4 step 5)
/// against real PostgreSQL (svc_relay) and real Kafka with <c>auto.create.topics.enable=false</c>:
/// InfraGlobal (stop the batch, never FAILED), TopicUnreachable (skip the aggregate, never FAILED)
/// and Row (skip the aggregate, FAILED at MaxAttempts).
/// </summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class FailureClassTests(PostgresFixture db, KafkaFixture kafka)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "TopicUnreachable: a missing topic holds back only its aggregate (no 30 s stall), other aggregates keep publishing; over 12 s of 500 ms passes only ~1 attempt per 5 s negative TTL counts (3, not 25), never FAILED beyond MaxAttempts; creating the topic publishes it in order")]
    public async Task MissingTopic_HoldsBackOnlyItsAggregate_ThenPublishedInOrder()
    {
        await OutboxRows.ClearAsync(db);
        var existing = ProbeRows.NewTopic();
        var missing = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(existing);
        await kafka.RegisterAsync(existing, ProbeRows.SchemaJson);
        await kafka.RegisterAsync(missing, ProbeRows.SchemaJson); // the subject exists, the topic does not

        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var a1 = await OutboxRows.InsertAsync(db, a, 1, T0, ProbeRows.Payload(a, 1), missing);
        var b1 = await OutboxRows.InsertAsync(db, b, 1, T0.AddMilliseconds(1), ProbeRows.Payload(b, 1), existing);
        var a2 = await OutboxRows.InsertAsync(db, a, 2, T0.AddMilliseconds(2), ProbeRows.Payload(a, 2), missing);
        var b2 = await OutboxRows.InsertAsync(db, b, 2, T0.AddMilliseconds(3), ProbeRows.Payload(b, 2), existing);

        var time = new ManualTime(DateTimeOffset.UtcNow);
        using var meters = new TestMeterFactory();
        using var errors = new ErrorKindListener(meters);
        using var publisher = new KafkaEventPublisher(
            new SchemaRegistryPayloadEncoder(kafka.CreateRegistryClient(), TestContracts.Probe),
            Options.Create(new RelayOptions { KafkaBootstrapServers = kafka.BootstrapServers, ProduceTimeoutMs = 15_000 }),
            time,
            NullLogger<KafkaEventPublisher>.Instance);
        const int MaxAttempts = 2;
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters), time, MaxAttempts, NullLogger.Instance);

        var stopwatch = Stopwatch.StartNew();
        var first = await RunAsync(processor);
        stopwatch.Stop();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5), "the preflight detects the missing topic instead of waiting ProduceTimeoutMs");
        first.StoppedOnTransientFailure.ShouldBeFalse("a missing topic is not a global outage");
        first.Published.ShouldBe(2);
        (await OutboxRows.GetAsync(db, b1)).Status.ShouldBe("PUBLISHED");
        (await OutboxRows.GetAsync(db, b2)).Status.ShouldBe("PUBLISHED");
        kafka.TopicExists(missing).ShouldBeFalse("nothing auto-created the topic");

        // 12 s of loop passes every 500 ms (24 batches). The preflight runs again only when the
        // 5 s negative cache expires (t = 5 s, 10 s): 3 counted attempts in total, not 25.
        Guid? b3 = null;
        var deferred = 0;
        for (var pass = 1; pass <= 24; pass++)
        {
            time.Advance(TimeSpan.FromMilliseconds(500));
            if (pass == 12)
            {
                b3 = await OutboxRows.InsertAsync(db, b, 3, T0.AddMilliseconds(4), ProbeRows.Payload(b, 3), existing);
            }

            deferred += (await RunAsync(processor)).Deferred;
        }

        var held = await OutboxRows.GetAsync(db, a1);
        held.Status.ShouldBe("PENDING", "TopicUnreachable never makes a row FAILED, even beyond MaxAttempts");
        held.Attempts.ShouldBe(3, "one counted attempt per preflight (t = 0, 5 s, 10 s), none while negatively cached");
        held.Attempts.ShouldBeGreaterThan(MaxAttempts);
        held.LastError.ShouldNotBeNull().ShouldContain(missing);
        deferred.ShouldBe(22, "the other passes skipped the aggregate untouched");
        (await OutboxRows.GetAsync(db, a2)).Attempts.ShouldBe(0, "EV-R3: the aggregate's later rows are not attempted");
        (await OutboxRows.GetAsync(db, b3!.Value)).Status.ShouldBe("PUBLISHED", "other aggregates keep publishing");
        errors.Count(RelayMetrics.ErrorKindTopic).ShouldBe(3);

        await kafka.CreateTopicAsync(missing);
        time.Advance(KafkaEventPublisher.NegativeTopicTtl + TimeSpan.FromSeconds(1));
        (await RunAsync(processor)).Published.ShouldBe(2);
        (await OutboxRows.GetAsync(db, a1)).Status.ShouldBe("PUBLISHED");
        (await OutboxRows.GetAsync(db, a2)).Status.ShouldBe("PUBLISHED");
        var versions = KafkaRead.ReadToEnd(kafka, missing).Select(e => e.AggregateVersion).ToList();
        versions.ShouldBe([1L, 2L]);
    }

    [Theory(DisplayName = "InfraGlobal: Schema Registry 401/403/408/429/500/503 stops the batch; the row is never FAILED (even at attempts 9 → 10) and later rows are untouched")]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task RegistryInfraStatuses_StopBatch_NeverFailed(int status)
    {
        var (head, other, outcome) = await RunAgainstFakeRegistryAsync(status);

        outcome.StoppedOnTransientFailure.ShouldBeTrue();
        outcome.StopCause.ShouldNotBeNull().ShouldContain($"HTTP {status}");
        var row = await OutboxRows.GetAsync(db, head);
        row.ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(10));
        (await OutboxRows.GetAsync(db, other)).Attempts.ShouldBe(0, "the batch stopped");
    }

    [Theory(DisplayName = "Row: Schema Registry 409/422 is about this schema: the row becomes FAILED at MaxAttempts and the batch continues with other aggregates")]
    [InlineData(409)]
    [InlineData(422)]
    public async Task RegistryRowStatuses_AreRowFailures(int status)
    {
        var (head, other, outcome) = await RunAgainstFakeRegistryAsync(status);

        outcome.StoppedOnTransientFailure.ShouldBeFalse();
        (await OutboxRows.GetAsync(db, head)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("FAILED"), r => r.Attempts.ShouldBe(10));
        (await OutboxRows.GetAsync(db, other)).Attempts.ShouldBe(1, "the next aggregate was attempted");
    }

    [Theory(DisplayName = "M1: generic Schema Registry 4xx that are not schema errors (unknown route 404/404, proxy HTML 404, 405, generic 400) are InfraGlobal: the batch stops, never FAILED")]
    [InlineData(404, 404, false)]
    [InlineData(404, 0, true)]
    [InlineData(405, 405, false)]
    [InlineData(400, 400, false)]
    [InlineData(404, -1, false)]
    public async Task RegistryGeneric4xx_AreInfraGlobal(int status, int errorCode, bool html)
    {
        var (head, other, outcome) = await RunAgainstFakeRegistryAsync(status, errorCode, html ? "<html><body>404 Not Found</body></html>" : null);

        outcome.StoppedOnTransientFailure.ShouldBeTrue();
        (await OutboxRows.GetAsync(db, head)).ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(10));
        (await OutboxRows.GetAsync(db, other)).Attempts.ShouldBe(0, "the batch stopped");
    }

    [Theory(DisplayName = "M1: schema-specific Confluent codes (40401, 40403, 409xx, 422xx) are Row failures: FAILED at MaxAttempts, the batch continues")]
    [InlineData(404, 40401)]
    [InlineData(404, 40403)]
    [InlineData(409, 40901)]
    [InlineData(422, 42201)]
    [InlineData(422, 42202)]
    public async Task RegistrySchemaCodes_AreRowFailures(int status, int errorCode)
    {
        var (head, other, outcome) = await RunAgainstFakeRegistryAsync(status, errorCode);

        outcome.StoppedOnTransientFailure.ShouldBeFalse();
        (await OutboxRows.GetAsync(db, head)).Status.ShouldBe("FAILED");
        (await OutboxRows.GetAsync(db, other)).Attempts.ShouldBe(1);
    }

    [Fact(DisplayName = "H1: held-back aggregates (≥ BatchSize head rows on a missing topic) are excluded from the batch query: healthy topics are published at once, no busy loop, attempts only per preflight; creating the topic publishes them in order")]
    public async Task HeldAggregates_DoNotStarveOtherTopics()
    {
        await OutboxRows.ClearAsync(db);
        var missing = ProbeRows.NewTopic();
        var healthy = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(healthy);
        await kafka.RegisterAsync(healthy, ProbeRows.SchemaJson);
        await kafka.RegisterAsync(missing, ProbeRows.SchemaJson);

        // 500 rows (= BatchSize) of 5 aggregates on the missing topic, then 30 rows of 3 aggregates on a healthy one.
        var start = new DateTimeOffset(DateTime.UtcNow.Ticks / 10 * 10, TimeSpan.Zero);
        var heldRows = ProbeRows.Interleaved(500, Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList(), seed: 11, start);
        var healthyRows = ProbeRows.Interleaved(30, Enumerable.Range(0, 3).Select(_ => Guid.CreateVersion7()).ToList(), seed: 12, start.AddSeconds(1));
        await ProbeRows.InsertAsync(db, missing, heldRows);
        await ProbeRows.InsertAsync(db, healthy, healthyRows);

        var options = new RelayOptions { BatchSize = 500, PollIntervalMs = 100, LeaderRetrySeconds = 1, ProduceTimeoutMs = 15_000 };
        await using var relay = new RelayInstance(db, kafka, options);
        var counting = new BatchCountingLeadership(relay.Leadership);
        var worker = new RelayWorker(counting, relay.Publisher, new RelayMetrics(new TestMeterFactory()), Options.Create(options), TimeProvider.System, NullLogger<RelayWorker>.Instance);
        var started = Stopwatch.StartNew();
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Poll.UntilAsync(async () => await ProbeRows.CountAsync(db, healthy, "PUBLISHED") == healthyRows.Count, TimeSpan.FromSeconds(10), "healthy topic published despite 500 held rows");
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

            // No busy loop: over 3 s with a 100 ms poll interval, about 30 passes (a spin is ~50/s).
            var before = counting.Batches;
            await Task.Delay(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            var passes = counting.Batches - before;
            passes.ShouldBeLessThanOrEqualTo(45, "the loop waits for the poll interval instead of spinning");

            (await ProbeRows.CountAsync(db, missing, "PENDING")).ShouldBe(500);
            (await ProbeRows.CountAsync(db, missing, "FAILED")).ShouldBe(0);
            var attempts = await db.ScalarAsync<long>("SELECT coalesce(sum(attempts), 0) FROM outbox.message WHERE topic = $1", missing);
            var maxPreflights = (long)(started.Elapsed.TotalSeconds / KafkaEventPublisher.NegativeTopicTtl.TotalSeconds) + 2;
            attempts.ShouldBeInRange(1, maxPreflights, "one counted attempt per preflight (negative TTL), not per pass");

            await kafka.CreateTopicAsync(missing);
            await Poll.UntilAsync(async () => await ProbeRows.CountAsync(db, missing, "PUBLISHED") == 500, TimeSpan.FromSeconds(30), "held rows published once the topic exists");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            worker.Dispose();
        }

        KafkaRead.ShouldHaveNoGapsPerKey(
            KafkaRead.ReadToEnd(kafka, missing),
            heldRows.GroupBy(r => r.AggregateId).ToDictionary(g => g.Key.ToString("D"), g => g.Max(r => r.Version)));
    }

    [Fact(DisplayName = "L1: a topic with a partition without leader is TopicUnreachable at the preflight (no 30 s produce wait), then Deferred while negatively cached")]
    public async Task Preflight_PartitionWithoutLeader_IsTopicUnreachable()
    {
        using var publisher = CreateOfflinePublisher();
        publisher.MetadataOverride = (topic, _) => Metadata(topic, ErrorCode.NoError, leader: -1);
        var message = FakeBatch.Message(Guid.CreateVersion7(), 1);

        var first = await publisher.PublishAsync(message, CancellationToken.None);
        first.ShouldSatisfyAllConditions(r => r.Kind.ShouldBe(PublishFailureKind.TopicUnreachable), r => r.CountsAsAttempt.ShouldBeTrue(), r => r.HoldUntil.ShouldNotBeNull());
        first.Error.ShouldNotBeNull().ShouldContain("without a leader");
        var second = await publisher.PublishAsync(message, CancellationToken.None);
        second.ShouldSatisfyAllConditions(r => r.Kind.ShouldBe(PublishFailureKind.TopicUnreachable), r => r.CountsAsAttempt.ShouldBeFalse());
    }

    [Fact(DisplayName = "L2: an invalid topic name (TopicException) at the preflight is a Row failure, as in the produce path, and is not cached as unreachable")]
    public async Task Preflight_InvalidTopic_IsRow()
    {
        using var publisher = CreateOfflinePublisher();
        var calls = 0;
        publisher.MetadataOverride = (topic, _) =>
        {
            calls++;
            return Metadata(topic, ErrorCode.TopicException, leader: 0);
        };
        var message = FakeBatch.Message(Guid.CreateVersion7(), 1);

        (await publisher.PublishAsync(message, CancellationToken.None)).Kind.ShouldBe(PublishFailureKind.Row);
        (await publisher.PublishAsync(message, CancellationToken.None)).Kind.ShouldBe(PublishFailureKind.Row);
        calls.ShouldBe(2);
    }

    [Fact(DisplayName = "L3: PublishAsync never throws: a non-Kafka exception from the metadata call is InfraGlobal with its cause")]
    public async Task Preflight_UnexpectedException_IsInfraGlobal()
    {
        using var publisher = CreateOfflinePublisher();
        publisher.MetadataOverride = (_, _) => throw new ObjectDisposedException("admin client");

        var result = await publisher.PublishAsync(FakeBatch.Message(Guid.CreateVersion7(), 1), CancellationToken.None);
        result.Kind.ShouldBe(PublishFailureKind.InfraGlobal);
        result.Error.ShouldNotBeNull().ShouldContain("ObjectDisposedException");
    }

    [Fact(DisplayName = "L3: PublishAsync never throws: a producer that cannot be created (invalid configuration) is InfraGlobal with its cause")]
    public async Task ProducerCreationFailure_IsInfraGlobal()
    {
        using var publisher = new KafkaEventPublisher(
            new ThrowingEncoder(new InvalidOperationException("unused")),
            Options.Create(new RelayOptions { KafkaBootstrapServers = "127.0.0.1:1", ProduceTimeoutMs = -5 }),
            TimeProvider.System,
            NullLogger<KafkaEventPublisher>.Instance);

        var result = await publisher.PublishAsync(FakeBatch.Message(Guid.CreateVersion7(), 1), CancellationToken.None);
        result.Kind.ShouldBe(PublishFailureKind.InfraGlobal);
        result.Error.ShouldNotBeNull().ShouldContain("producer could not be created");
    }

    [Fact(DisplayName = "Row (N3): headers jsonb that is valid JSON but not an object ([] , \"x\", null) is a row failure: FAILED at MaxAttempts while other aggregates keep publishing")]
    public async Task NonObjectHeaders_AreRowFailures()
    {
        await OutboxRows.ClearAsync(db);
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        await kafka.RegisterAsync(topic, ProbeRows.SchemaJson);

        var bad = new List<Guid>();
        var good = new List<Guid>();
        var i = 0;
        foreach (var headers in new[] { "[]", "\"x\"", "null" })
        {
            var x = Guid.CreateVersion7();
            bad.Add(await OutboxRows.InsertAsync(db, x, 1, T0.AddMilliseconds(i++), ProbeRows.Payload(x, 1), topic, attempts: 9, headers: headers));
            var y = Guid.CreateVersion7();
            good.Add(await OutboxRows.InsertAsync(db, y, 1, T0.AddMilliseconds(i++), ProbeRows.Payload(y, 1), topic));
        }

        using var meters = new TestMeterFactory();
        using var publisher = CreatePublisher(new SchemaRegistryPayloadEncoder(kafka.CreateRegistryClient(), TestContracts.Probe));
        var outcome = await RunAsync(new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance));

        outcome.StoppedOnTransientFailure.ShouldBeFalse("bad headers must not freeze the relay");
        outcome.Published.ShouldBe(3);
        foreach (var id in bad)
        {
            var row = await OutboxRows.GetAsync(db, id);
            row.Status.ShouldBe("FAILED");
            row.LastError.ShouldNotBeNull().ShouldContain("must be a JSON object");
        }

        foreach (var id in good)
        {
            (await OutboxRows.GetAsync(db, id)).Status.ShouldBe("PUBLISHED");
        }
    }

    [Theory(DisplayName = "Row (N3): RelayKafkaHeaders rejects headers that are not a JSON object with a row failure, not an InvalidOperationException")]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("null")]
    [InlineData("42")]
    public void Headers_NotAnObject_RowFailure(string headers)
    {
        var message = FakeBatch.Message(Guid.CreateVersion7(), 1) with { HeadersJson = headers };
        var ex = Should.Throw<PayloadEncodingException>(() => RelayKafkaHeaders.Build(message));
        ex.IsTransient.ShouldBeFalse();
        ex.Message.ShouldContain("must be a JSON object");
    }

    [Fact(DisplayName = "Row (N3): an unexpected non-I/O exception preparing the message is a Row failure; an I/O one is InfraGlobal")]
    public async Task UnexpectedPreparationException_Classified()
    {
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        var message = FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = topic };

        using (var publisher = CreatePublisher(new ThrowingEncoder(new InvalidOperationException("bug"))))
        {
            (await publisher.PublishAsync(message, CancellationToken.None)).Kind.ShouldBe(PublishFailureKind.Row);
        }

        using (var publisher = CreatePublisher(new ThrowingEncoder(new IOException("disk"))))
        {
            (await publisher.PublishAsync(message, CancellationToken.None)).Kind.ShouldBe(PublishFailureKind.InfraGlobal);
        }
    }

    [Theory(DisplayName = "Produce errors: topic missing/unauthorised ⇒ TopicUnreachable (and the topic is cached as unreachable); message-specific ⇒ Row; anything else ⇒ InfraGlobal")]
    [InlineData(ErrorCode.TopicAuthorizationFailed, PublishFailureKind.TopicUnreachable)]
    [InlineData(ErrorCode.UnknownTopicOrPart, PublishFailureKind.TopicUnreachable)]
    [InlineData(ErrorCode.Local_UnknownTopic, PublishFailureKind.TopicUnreachable)]
    [InlineData(ErrorCode.MsgSizeTooLarge, PublishFailureKind.Row)]
    [InlineData(ErrorCode.InvalidRecord, PublishFailureKind.Row)]
    [InlineData(ErrorCode.Local_MsgTimedOut, PublishFailureKind.InfraGlobal)]
    [InlineData(ErrorCode.Local_AllBrokersDown, PublishFailureKind.InfraGlobal)]
    public async Task ProduceErrors_Classified(ErrorCode code, PublishFailureKind expected)
    {
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic); // exists: only the produce error can make it unreachable
        using var publisher = CreatePublisher(new ThrowingEncoder(new InvalidOperationException("never mind")));

        var result = publisher.ProduceFailure(topic, new Error(code));
        result.Kind.ShouldBe(expected);
        result.Error.ShouldNotBeNull().ShouldContain(topic);

        // A topic error is remembered: the next row of that topic is held back without producing,
        // and without counting an attempt (negative cache).
        var next = await publisher.PublishAsync(FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = topic }, CancellationToken.None);
        next.Kind.ShouldBe(expected == PublishFailureKind.TopicUnreachable ? PublishFailureKind.TopicUnreachable : PublishFailureKind.Row);
        next.CountsAsAttempt.ShouldBe(expected != PublishFailureKind.TopicUnreachable);
    }

    [Fact(DisplayName = "N6: the Kafka producer is created lazily, on the first publish (a passive instance opens no Kafka connection)")]
    public async Task Producer_IsCreatedLazily()
    {
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        using var publisher = CreatePublisher(new ThrowingEncoder(new InvalidOperationException("never mind")));
        publisher.ProducerCreated.ShouldBeFalse();
        await publisher.PublishAsync(FakeBatch.Message(Guid.CreateVersion7(), 1) with { Topic = topic }, CancellationToken.None);
        publisher.ProducerCreated.ShouldBeTrue();
    }

    private static KafkaEventPublisher CreateOfflinePublisher() =>
        new(new ThrowingEncoder(new InvalidOperationException("unused")), Options.Create(new RelayOptions { KafkaBootstrapServers = "127.0.0.1:1" }), TimeProvider.System, NullLogger<KafkaEventPublisher>.Instance);

    private static Confluent.Kafka.Metadata Metadata(string topic, ErrorCode topicError, int leader) =>
        new(
            [new BrokerMetadata(1, "127.0.0.1", 1)],
            [new TopicMetadata(topic, [new PartitionMetadata(0, leader, [1], [], new Error(ErrorCode.NoError))], new Error(topicError))],
            1,
            "test");

    private async Task<(Guid Head, Guid Other, BatchOutcome Outcome)> RunAgainstFakeRegistryAsync(int status, int? errorCode = null, string? rawBody = null)
    {
        await OutboxRows.ClearAsync(db);
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var head = await OutboxRows.InsertAsync(db, a, 1, T0, ProbeRows.Payload(a, 1), topic, attempts: 9);
        var other = await OutboxRows.InsertAsync(db, b, 1, T0.AddMilliseconds(1), ProbeRows.Payload(b, 1), topic);

        using var server = new FakeRegistryServer(status, errorCode, rawBody);
        using var registry = new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = server.Url, RequestTimeoutMs = 2_000, MaxRetries = 1, RetriesWaitMs = 10, RetriesMaxWaitMs = 20 });
        using var meters = new TestMeterFactory();
        using var publisher = CreatePublisher(new SchemaRegistryPayloadEncoder(registry, TestContracts.Probe));
        var outcome = await RunAsync(new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, 10, NullLogger.Instance));
        server.Requests.ShouldBeGreaterThan(0);
        return (head, other, outcome);
    }

    private KafkaEventPublisher CreatePublisher(IPayloadEncoder encoder) =>
        new(encoder, Options.Create(new RelayOptions { KafkaBootstrapServers = kafka.BootstrapServers, ProduceTimeoutMs = 15_000 }), TimeProvider.System, NullLogger<KafkaEventPublisher>.Instance);

    private async Task<BatchOutcome> RunAsync(OutboxBatchProcessor processor)
    {
        await using var lease = await OutboxRows.AcquireAsync(db);
        await using var batch = await lease.BeginBatchAsync(500, CancellationToken.None);
        return await processor.ProcessAsync(batch, CancellationToken.None);
    }

    private sealed class ThrowingEncoder(Exception exception) : IPayloadEncoder
    {
        public Task<byte[]> EncodeAsync(OutboxMessage message, CancellationToken cancellationToken) => Task.FromException<byte[]>(exception);
    }
}

/// <summary>Counts the batches begun through the leases it hands out.</summary>
internal sealed class BatchCountingLeadership(IRelayLeadership inner) : IRelayLeadership
{
    private int _batches;

    public int Batches => Volatile.Read(ref _batches);

    public async Task<IRelayLease?> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var lease = await inner.TryAcquireAsync(cancellationToken);
        return lease is null ? null : new CountingLease(lease, this);
    }

    private sealed class CountingLease(IRelayLease inner, BatchCountingLeadership owner) : IRelayLease
    {
        public Task<bool> WaitForWakeUpAsync(TimeSpan timeout, CancellationToken cancellationToken) => inner.WaitForWakeUpAsync(timeout, cancellationToken);

        public Task<IOutboxBatch> BeginBatchAsync(int batchSize, IReadOnlyCollection<Guid> excludedAggregates, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner._batches);
            return inner.BeginBatchAsync(batchSize, excludedAggregates, cancellationToken);
        }

        public Task<int> PurgePublishedAsync(DateTimeOffset cutoff, int maxRows, CancellationToken cancellationToken) => inner.PurgePublishedAsync(cutoff, maxRows, cancellationToken);

        public Task<PendingStats> GetPendingStatsAsync(CancellationToken cancellationToken) => inner.GetPendingStatsAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

/// <summary>Counts <c>nexus_outbox_errors_total</c> by <c>kind</c> for one meter factory.</summary>
internal sealed class ErrorKindListener : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Dictionary<string, long> _counts = [];

    public ErrorKindListener(IMeterFactory factory)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Scope == factory && instrument.Name == RelayMetrics.ErrorsTotalName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "kind" && tag.Value is string kind)
                {
                    lock (_counts)
                    {
                        _counts[kind] = _counts.GetValueOrDefault(kind) + value;
                    }
                }
            }
        });
        _listener.Start();
    }

    public long Count(string kind)
    {
        lock (_counts)
        {
            return _counts.GetValueOrDefault(kind);
        }
    }

    public void Dispose() => _listener.Dispose();
}

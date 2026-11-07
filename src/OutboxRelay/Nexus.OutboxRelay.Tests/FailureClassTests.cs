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

    private async Task<(Guid Head, Guid Other, BatchOutcome Outcome)> RunAgainstFakeRegistryAsync(int status)
    {
        await OutboxRows.ClearAsync(db);
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var head = await OutboxRows.InsertAsync(db, a, 1, T0, ProbeRows.Payload(a, 1), topic, attempts: 9);
        var other = await OutboxRows.InsertAsync(db, b, 1, T0.AddMilliseconds(1), ProbeRows.Payload(b, 1), topic);

        using var server = new FakeRegistryServer(status);
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

using System.Text;
using Avro;
using Avro.Generic;
using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Nexus.BuildingBlocks.Testing;
using Nexus.OutboxRelay.Publishing;
using Nexus.OutboxRelay.Tests.Infrastructure;
using Npgsql;
using Shouldly;
using Schema = Avro.Schema;

namespace Nexus.OutboxRelay.Tests;

/// <summary>
/// End to end through a real Kafka broker and a real Confluent Schema Registry
/// (conventions-events.md §2, §4.4, §6).
/// </summary>
[Collection(RelayDatabaseTests.Name)]
public sealed class KafkaPublishingTests(PostgresFixture db, KafkaFixture kafka)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    // A legacy shape registered first, so the subject's version numbers and the files' v<n> differ.
    private const string ProbeV0 =
        """
        {"type":"record","name":"RelayProbeCreated","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"probeId","type":{"type":"string","logicalType":"uuid"}},
          {"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}},
          {"name":"legacyCode","type":"string"}]}
        """;

    // BACKWARD-compatible with V0 (a field removed).
    private const string ProbeV1 = ProbeRows.SchemaJson;

    // BACKWARD-compatible evolution: a new optional field with a default.
    private const string ProbeV2 =
        """
        {"type":"record","name":"RelayProbeCreated","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"probeId","type":{"type":"string","logicalType":"uuid"}},
          {"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}},
          {"name":"note","type":["null","string"],"default":null}]}
        """;

    // Incompatible with V1 (a new required field): lives in a new topic "...created.v2" (§6).
    private const string ProbeIncompatibleV2 =
        """
        {"type":"record","name":"RelayProbeCreated","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"probeId","type":{"type":"string","logicalType":"uuid"}},
          {"name":"total","type":{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}},
          {"name":"currency","type":"string"}]}
        """;

    [Fact(DisplayName = "§2/§4.4: an event written by the real UnitOfWork is produced with key = aggregate_id, every nexus-* header, and an Avro value that round-trips")]
    public async Task UnitOfWorkEvent_IsPublishedWithAllHeaders()
    {
        await OutboxRows.ClearAsync(db);
        const string Topic = "nexus.test.test-event.created";
        var avsc = await File.ReadAllTextAsync(Path.Combine(RepoRoot.Find("contracts/events/_test"), "test", "test-event.created.v1.avsc"), TestContext.Current.CancellationToken);
        await kafka.CreateTopicAsync(Topic);
        var (schemaId, _) = await kafka.RegisterAsync(Topic, avsc);

        // Arrange through the real write path (svc_platform, OutboxWriter, EV-R2 validation).
        var userId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var eventAggregateId = Guid.CreateVersion7();
        var writer = new OutboxWriter(new FileSystemEventSchemaProvider(RepoRoot.Find("contracts/events/_test")), new TestIdGenerator(), RoundedSystemTimeProvider.Instance);
        await using (var uow = new UnitOfWork(db.GetServiceDataSource("svc_platform"), new TestCurrentUser(userId), new CorrelationContext { CorrelationId = correlationId }, writer))
        {
            await uow.BeginAsync(TestContext.Current.CancellationToken);
            uow.AddEvent(new TestEvent(eventAggregateId, "héllo"), aggregateVersion: 7);
            await uow.CommitAsync(TestContext.Current.CancellationToken);
        }

        var (rowId, headersJson) = await ReadRowAsync(eventAggregateId);

        // The relay reads the same contracts root the writer validated against (ADR-0018).
        using var publisher = CreatePublisher(RepoRoot.Find("contracts/events/_test"));
        var outcome = await RunBatchAsync(publisher);
        outcome.Published.ShouldBe(1);
        (await OutboxRows.GetAsync(db, rowId)).Status.ShouldBe("PUBLISHED");

        var message = kafka.ReadAll(Topic, expected: 1, TimeSpan.FromSeconds(30)).ShouldHaveSingleItem().Message;
        message.Key.ShouldBe(eventAggregateId.ToString("D"));

        var headers = message.Headers.ToDictionary(h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()));
        using var stored = System.Text.Json.JsonDocument.Parse(headersJson);
        headers.Keys.ShouldBe(
            [
                "nexus-event-id", "nexus-event-type", "nexus-aggregate-type", "nexus-aggregate-id", "nexus-aggregate-version",
                "nexus-occurred-at", "nexus-actor-user-id", "nexus-schema-version", "nexus-correlation-id", "nexus-causation-id",
                "nexus-legal-entity-id",
            ],
            ignoreOrder: false);
        headers["nexus-event-id"].ShouldBe(rowId.ToString("D"));
        headers["nexus-event-type"].ShouldBe("test.test-event.created");
        headers["nexus-aggregate-type"].ShouldBe("test-event");
        headers["nexus-aggregate-id"].ShouldBe(eventAggregateId.ToString("D"));
        headers["nexus-aggregate-version"].ShouldBe("7");
        headers["nexus-schema-version"].ShouldBe("1");
        headers["nexus-occurred-at"].ShouldBe(stored.RootElement.GetProperty(OutboxHeaders.OccurredAt).GetString());
        headers["nexus-actor-user-id"].ShouldBe(userId.ToString("D"));
        headers["nexus-correlation-id"].ShouldBe(correlationId.ToString("D"));
        headers["nexus-causation-id"].ShouldBe(stored.RootElement.GetProperty(OutboxHeaders.CausationId).GetString());
        headers["nexus-legal-entity-id"].ShouldBe(string.Empty, "TestEvent has no legal-entity scope (JSON null)");

        var (wireId, datum) = AvroJson.DecodeWire(message.Value, Schema.Parse(avsc));
        wireId.ShouldBe(schemaId);
        var record = datum.ShouldBeOfType<GenericRecord>();
        record["testEventId"].ShouldBe(eventAggregateId);
        record["name"].ShouldBe("héllo");
    }

    [Fact(DisplayName = "ADR-0018: same subject, v1.avsc = subject version 2 and v2.avsc = subject version 3 ⇒ each row is encoded with the id of its own .avsc content and decodes back; decimals with \\u0000 bytes round-trip")]
    public async Task SameSubject_EachRowGetsTheIdOfItsOwnContent()
    {
        await OutboxRows.ClearAsync(db);
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        var (_, version0) = await kafka.RegisterAsync(topic, ProbeV0);
        var (idV1, versionV1) = await kafka.RegisterAsync(topic, ProbeV1);
        var (idV2, versionV2) = await kafka.RegisterAsync(topic, ProbeV2);
        (version0, versionV1, versionV2).ShouldBe((1, 2, 3), "the subject's numbering is not the files' v<n>");
        idV1.ShouldNotBe(idV2);

        var contracts = TestContracts.Create(
            ("test/relay-probe.created.v1.avsc", ProbeV1),
            ("test/relay-probe.created.v2.avsc", ProbeV2));

        var v1 = (RecordSchema)Schema.Parse(ProbeV1);
        var v2 = (RecordSchema)Schema.Parse(ProbeV2);
        var aggregate = Guid.CreateVersion7();
        var legalEntity = Guid.CreateVersion7();

        // 16.0000 at scale 4 = 160000 = 0x02 0x71 0x00: a \u0000 escape in the JSON (ADR-0013).
        var p1 = new GenericRecord(v1);
        p1.Add("probeId", aggregate);
        p1.Add("amount", new AvroDecimal(16.0000m));
        var json1 = AvroJson.Encode(v1, p1);
        json1.ShouldContain("\\u0000");

        var p2 = new GenericRecord(v2);
        p2.Add("probeId", aggregate);
        p2.Add("amount", new AvroDecimal(-1234.5678m));
        p2.Add("note", "second");
        var json2 = AvroJson.Encode(v2, p2);

        await OutboxRows.InsertAsync(db, aggregate, 1, T0.AddMilliseconds(1), json1, topic, schemaVersion: 1, headers: OutboxRows.Headers(legalEntity));
        await OutboxRows.InsertAsync(db, aggregate, 2, T0.AddMilliseconds(2), json2, topic, schemaVersion: 2, headers: OutboxRows.Headers(legalEntity));

        using var publisher = CreatePublisher(contracts);
        (await RunBatchAsync(publisher)).Published.ShouldBe(2);

        var messages = kafka.ReadAll(topic, expected: 2, TimeSpan.FromSeconds(30));
        messages.Count.ShouldBe(2);

        var (wire1, d1) = AvroJson.DecodeWire(messages[0].Message.Value, v1);
        wire1.ShouldBe(idV1, "schema_version 1 ⇒ the id of v1.avsc's content (subject version 2), not of subject version 1");
        var r1 = (GenericRecord)d1;
        r1["probeId"].ShouldBe(aggregate);
        ((AvroDecimal)r1["amount"]).ShouldBe(new AvroDecimal(16.0000m));

        var (wire2, d2) = AvroJson.DecodeWire(messages[1].Message.Value, v2);
        wire2.ShouldBe(idV2, "schema_version 2 ⇒ the id of v2.avsc's content (subject version 3)");
        var r2 = (GenericRecord)d2;
        ((AvroDecimal)r2["amount"]).ShouldBe(new AvroDecimal(-1234.5678m));
        r2["note"].ShouldBe("second");

        Encoding.UTF8.GetString(messages[0].Message.Headers.GetLastBytes(RelayKafkaHeaders.LegalEntityId)).ShouldBe(legalEntity.ToString("D"));
        Encoding.UTF8.GetString(messages[1].Message.Headers.GetLastBytes(RelayKafkaHeaders.AggregateVersion)).ShouldBe("2");
        Encoding.UTF8.GetString(messages[1].Message.Headers.GetLastBytes(RelayKafkaHeaders.SchemaVersion)).ShouldBe("2");
    }

    [Fact(DisplayName = "ADR-0018 / §6: incompatible change in a new topic '.v2' (its subject starts at version 1, schema_version = 2) ⇒ encoded with the id of v2.avsc; the v1 topic keeps v1.avsc")]
    public async Task NewV2Topic_SubjectVersionOneIsSchemaVersionTwo()
    {
        await OutboxRows.ClearAsync(db);
        var topicV1 = ProbeRows.NewTopic();
        var topicV2 = topicV1 + ".v2";
        await kafka.CreateTopicAsync(topicV1);
        await kafka.CreateTopicAsync(topicV2);
        var (idV1, _) = await kafka.RegisterAsync(topicV1, ProbeV1);
        var (idNew, subjectVersion) = await kafka.RegisterAsync(topicV2, ProbeIncompatibleV2);
        subjectVersion.ShouldBe(1, "a new topic's subject starts at version 1");

        var contracts = TestContracts.Create(
            ("test/relay-probe.created.v1.avsc", ProbeV1),
            ("test/relay-probe.created.v2.avsc", ProbeIncompatibleV2));

        var schemaNew = (RecordSchema)Schema.Parse(ProbeIncompatibleV2);
        var aggregate = Guid.CreateVersion7();
        var record = new GenericRecord(schemaNew);
        record.Add("probeId", aggregate);
        record.Add("total", new AvroDecimal(42.5000m));
        record.Add("currency", "CHF");
        var oldRow = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0, ProbeRows.Payload(Guid.CreateVersion7(), 1), topicV1, schemaVersion: 1);
        var newRow = await OutboxRows.InsertAsync(db, aggregate, 1, T0.AddMilliseconds(1), AvroJson.Encode(schemaNew, record), topicV2, schemaVersion: 2);

        using var publisher = CreatePublisher(contracts);
        (await RunBatchAsync(publisher)).Published.ShouldBe(2);
        (await OutboxRows.GetAsync(db, oldRow)).Status.ShouldBe("PUBLISHED");
        (await OutboxRows.GetAsync(db, newRow)).Status.ShouldBe("PUBLISHED");

        var (wireOld, _) = AvroJson.DecodeWire(kafka.ReadAll(topicV1, expected: 1, TimeSpan.FromSeconds(30)).ShouldHaveSingleItem().Message.Value, ProbeRows.Schema);
        wireOld.ShouldBe(idV1);

        var (wireNew, datum) = AvroJson.DecodeWire(kafka.ReadAll(topicV2, expected: 1, TimeSpan.FromSeconds(30)).ShouldHaveSingleItem().Message.Value, schemaNew);
        wireNew.ShouldBe(idNew);
        var decoded = (GenericRecord)datum;
        decoded["probeId"].ShouldBe(aggregate);
        ((AvroDecimal)decoded["total"]).ShouldBe(new AvroDecimal(42.5000m));
        decoded["currency"].ShouldBe("CHF");
    }

    [Fact(DisplayName = "ADR-0018 §4: a schema_version without a local .avsc is a row failure naming the expected file; other aggregates of the batch are published")]
    public async Task MissingLocalSchema_IsRowFailure()
    {
        await OutboxRows.ClearAsync(db);
        var topic = ProbeRows.NewTopic();
        await kafka.CreateTopicAsync(topic);
        await kafka.RegisterAsync(topic, ProbeV1);
        var missing = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0, "{}", topic, schemaVersion: 5);
        var other = Guid.CreateVersion7();
        var fine = await OutboxRows.InsertAsync(db, other, 1, T0.AddMilliseconds(1), ProbeRows.Payload(other, 1), topic, schemaVersion: 1);

        using var publisher = CreatePublisher(TestContracts.Probe);
        var outcome = await RunBatchAsync(publisher);

        outcome.StoppedOnTransientFailure.ShouldBeFalse("a missing file is about this row, not the infrastructure");
        var row = await OutboxRows.GetAsync(db, missing);
        row.Status.ShouldBe("PENDING");
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldNotBeNull().ShouldContain(Path.Combine("test", "relay-probe.created.v5.avsc"));
        (await OutboxRows.GetAsync(db, fine)).Status.ShouldBe("PUBLISHED");
    }

    [Fact(DisplayName = "ADR-0018 §4: a local .avsc whose content is not registered (40403) or whose subject does not exist (40401) is a row failure; failures are not cached, so registering it lets the next batch publish")]
    public async Task UnregisteredContent_IsRowFailure_NotCached()
    {
        await OutboxRows.ClearAsync(db);
        var notInSubject = ProbeRows.NewTopic();
        var noSubject = ProbeRows.NewTopic();
        var registered = ProbeRows.NewTopic();
        foreach (var t in new[] { notInSubject, noSubject, registered })
        {
            await kafka.CreateTopicAsync(t);
        }

        await kafka.RegisterAsync(notInSubject, ProbeV1); // the subject exists, but not with v1.avsc's current content
        await kafka.RegisterAsync(registered, ProbeV2);

        // v1.avsc edited in place (compatible change) but not registered yet in every subject.
        var contracts = TestContracts.Create(("test/relay-probe.created.v1.avsc", ProbeV2));
        var v2 = (RecordSchema)Schema.Parse(ProbeV2);
        string Payload(Guid aggregate)
        {
            var record = new GenericRecord(v2);
            record.Add("probeId", aggregate);
            record.Add("amount", new AvroDecimal(1.0000m));
            record.Add("note", null);
            return AvroJson.Encode(v2, record);
        }

        var (a, b, c) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var rowA = await OutboxRows.InsertAsync(db, a, 1, T0, Payload(a), notInSubject);
        var rowB = await OutboxRows.InsertAsync(db, b, 1, T0.AddMilliseconds(1), Payload(b), noSubject);
        var rowC = await OutboxRows.InsertAsync(db, c, 1, T0.AddMilliseconds(2), Payload(c), registered);

        using var publisher = CreatePublisher(contracts);
        var outcome = await RunBatchAsync(publisher);

        outcome.StoppedOnTransientFailure.ShouldBeFalse();
        outcome.Published.ShouldBe(1);
        var failedA = await OutboxRows.GetAsync(db, rowA);
        failedA.ShouldSatisfyAllConditions(r => r.Status.ShouldBe("PENDING"), r => r.Attempts.ShouldBe(1));
        failedA.LastError.ShouldNotBeNull().ShouldContain($"not registered in subject '{notInSubject}-value' (error 40403)");
        var failedB = await OutboxRows.GetAsync(db, rowB);
        failedB.Attempts.ShouldBe(1);
        failedB.LastError.ShouldNotBeNull().ShouldContain($"not registered in subject '{noSubject}-value' (error 40401)");
        (await OutboxRows.GetAsync(db, rowC)).Status.ShouldBe("PUBLISHED");

        // Same encoder (same cache): registering the content fixes the row's next attempt.
        await kafka.RegisterAsync(notInSubject, ProbeV2);
        (await RunBatchAsync(publisher)).Published.ShouldBe(1);
        (await OutboxRows.GetAsync(db, rowA)).Status.ShouldBe("PUBLISHED");
        (await OutboxRows.GetAsync(db, rowB)).Attempts.ShouldBe(2);
    }

    [Fact(DisplayName = "§4.4: Schema Registry unreachable is a transient failure: no message is lost, the row stays PENDING with attempts+1")]
    public async Task RegistryDown_IsTransient()
    {
        await OutboxRows.ClearAsync(db);
        var aggregate = Guid.CreateVersion7();
        var id = await OutboxRows.InsertAsync(db, aggregate, 1, T0, ProbeRows.Payload(aggregate, 1));

        // Port 9 (discard) on loopback: connection refused. The local .avsc exists, so the failure is the registry's.
        using var registry = new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = "http://127.0.0.1:9", RequestTimeoutMs = 2000, MaxRetries = 1 });
        using var publisher = new KafkaEventPublisher(new SchemaRegistryPayloadEncoder(registry, TestContracts.Probe), Options(), NullLogger<KafkaEventPublisher>.Instance);
        var outcome = await RunBatchAsync(publisher);

        outcome.StoppedOnTransientFailure.ShouldBeTrue();
        var row = await OutboxRows.GetAsync(db, id);
        row.Status.ShouldBe("PENDING");
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldNotBeNull().ShouldContain("unavailable");
    }

    private KafkaEventPublisher CreatePublisher(string contractsDirectory) =>
        new(new SchemaRegistryPayloadEncoder(kafka.CreateRegistryClient(), contractsDirectory), Options(), NullLogger<KafkaEventPublisher>.Instance);

    private IOptions<RelayOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new RelayOptions
        {
            KafkaBootstrapServers = kafka.BootstrapServers,
            SchemaRegistryUrl = kafka.SchemaRegistryUrl,
            ProduceTimeoutMs = 15_000,
        });

    private async Task<BatchOutcome> RunBatchAsync(IEventPublisher publisher)
    {
        using var meters = new TestMeterFactory();
        var processor = new OutboxBatchProcessor(publisher, new RelayMetrics(meters), TimeProvider.System, maxAttempts: 10, NullLogger.Instance);
        await using var lease = await OutboxRows.AcquireAsync(db);
        await using var batch = await lease.BeginBatchAsync(500, CancellationToken.None);
        return await processor.ProcessAsync(batch, CancellationToken.None);
    }

    private async Task<(Guid Id, string Headers)> ReadRowAsync(Guid aggregateId)
    {
        await using var command = db.Admin.CreateCommand("SELECT id, headers::text FROM outbox.message WHERE aggregate_id = $1");
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = aggregateId });
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).ShouldBeTrue();
        return (reader.GetGuid(0), reader.GetString(1));
    }
}

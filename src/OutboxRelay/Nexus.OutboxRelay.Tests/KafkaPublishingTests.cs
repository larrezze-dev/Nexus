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

    private const string ProbeV1 =
        """
        {"type":"record","name":"RelayProbeCreated","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"probeId","type":{"type":"string","logicalType":"uuid"}},
          {"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}}]}
        """;

    // BACKWARD-compatible evolution: a new optional field with a default.
    private const string ProbeV2 =
        """
        {"type":"record","name":"RelayProbeCreated","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"probeId","type":{"type":"string","logicalType":"uuid"}},
          {"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":19,"scale":4}},
          {"name":"note","type":["null","string"],"default":null}]}
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

        using var publisher = CreatePublisher();
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

    [Fact(DisplayName = "§4.4: the encoder uses the subject version = the row's schema_version (v1 and v2 rows get their own schema id); decimals with \\u0000 bytes round-trip")]
    public async Task EncoderPinsRowSchemaVersion()
    {
        await OutboxRows.ClearAsync(db);
        var topic = $"nexus.test.relay-probe-{Guid.NewGuid():N}.created";
        await kafka.CreateTopicAsync(topic);
        var (idV1, versionV1) = await kafka.RegisterAsync(topic, ProbeV1);
        var (idV2, versionV2) = await kafka.RegisterAsync(topic, ProbeV2);
        (versionV1, versionV2).ShouldBe((1, 2));
        idV1.ShouldNotBe(idV2);

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

        using var publisher = CreatePublisher();
        (await RunBatchAsync(publisher)).Published.ShouldBe(2);

        var messages = kafka.ReadAll(topic, expected: 2, TimeSpan.FromSeconds(30));
        messages.Count.ShouldBe(2);

        var (wire1, d1) = AvroJson.DecodeWire(messages[0].Message.Value, v1);
        wire1.ShouldBe(idV1);
        var r1 = (GenericRecord)d1;
        r1["probeId"].ShouldBe(aggregate);
        ((AvroDecimal)r1["amount"]).ShouldBe(new AvroDecimal(16.0000m));

        var (wire2, d2) = AvroJson.DecodeWire(messages[1].Message.Value, v2);
        wire2.ShouldBe(idV2);
        var r2 = (GenericRecord)d2;
        ((AvroDecimal)r2["amount"]).ShouldBe(new AvroDecimal(-1234.5678m));
        r2["note"].ShouldBe("second");

        Encoding.UTF8.GetString(messages[0].Message.Headers.GetLastBytes(RelayKafkaHeaders.LegalEntityId)).ShouldBe(legalEntity.ToString("D"));
        Encoding.UTF8.GetString(messages[1].Message.Headers.GetLastBytes(RelayKafkaHeaders.AggregateVersion)).ShouldBe("2");
    }

    [Fact(DisplayName = "§4.5: a schema_version not registered in the Schema Registry is a per-row (non-transient) failure; the row stays PENDING with attempts+1")]
    public async Task UnknownSchemaVersion_IsPermanentFailure()
    {
        await OutboxRows.ClearAsync(db);
        var topic = $"nexus.test.relay-probe-{Guid.NewGuid():N}.created";
        await kafka.CreateTopicAsync(topic);
        await kafka.RegisterAsync(topic, ProbeV1);
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0, "{}", topic, schemaVersion: 5);

        using var publisher = CreatePublisher();
        var outcome = await RunBatchAsync(publisher);

        outcome.StoppedOnTransientFailure.ShouldBeFalse();
        var row = await OutboxRows.GetAsync(db, id);
        row.Status.ShouldBe("PENDING");
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldNotBeNull().ShouldContain("version 5");
    }

    [Fact(DisplayName = "§4.4: Schema Registry unreachable is a transient failure: no message is lost, the row stays PENDING with attempts+1")]
    public async Task RegistryDown_IsTransient()
    {
        await OutboxRows.ClearAsync(db);
        var id = await OutboxRows.InsertAsync(db, Guid.CreateVersion7(), 1, T0);

        // Port 9 (discard) on loopback: connection refused.
        using var registry = new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = "http://127.0.0.1:9", RequestTimeoutMs = 2000, MaxRetries = 0 });
        using var publisher = new KafkaEventPublisher(new SchemaRegistryPayloadEncoder(registry), Options(), NullLogger<KafkaEventPublisher>.Instance);
        var outcome = await RunBatchAsync(publisher);

        outcome.StoppedOnTransientFailure.ShouldBeTrue();
        var row = await OutboxRows.GetAsync(db, id);
        row.Status.ShouldBe("PENDING");
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldNotBeNull().ShouldContain("unavailable");
    }

    private KafkaEventPublisher CreatePublisher() =>
        new(new SchemaRegistryPayloadEncoder(kafka.CreateRegistryClient()), Options(), NullLogger<KafkaEventPublisher>.Instance);

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

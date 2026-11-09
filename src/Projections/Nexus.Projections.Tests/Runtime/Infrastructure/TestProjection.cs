using Avro;
using Avro.Generic;
using Confluent.Kafka;
using Nexus.BuildingBlocks.Messaging;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Tests.Runtime.Infrastructure;

/// <summary>
/// A projection owned by the runtime tests (the real ones are tested separately): one row per event into
/// a per-test <c>ReplacingMergeTree(_aggregate_version)</c> table keyed by (legal_entity_id, aggregate_id).
/// <list type="bullet">
///   <item>payload <c>name = "explode"</c> → <see cref="Map"/> throws (a deterministic, per-record failure);</item>
///   <item>payload <c>name = "bad-type"</c> → a row whose UInt64 column holds a string: ClickHouse (the client's
///   RowBinary serializer) REJECTS the batch (decision D1);</item>
///   <item>a topic not starting with <see cref="OwnTopicPrefix"/> (when set) → no rows (pattern tests share the broker).</item>
/// </list>
/// </summary>
internal sealed class TestProjection : IProjection
{
    public const string SchemaJson =
        """{"type":"record","name":"RuntimeTestEvent","namespace":"nexus.test","fields":[{"name":"name","type":"string"}]}""";

    public static readonly RecordSchema Schema = (RecordSchema)Avro.Schema.Parse(SchemaJson);

    private static readonly string[] Columns = ["legal_entity_id", "aggregate_id", "name", "_event_id", "_aggregate_version", "_ingested_at"];

    private int _mapCalls;

    public TestProjection(string name, ProjectionSubscription subscription, string? ownTopicPrefix = null)
    {
        Name = name;
        TableName = $"nexus_analytics.{name}";
        Subscription = subscription;
        OwnTopicPrefix = ownTopicPrefix;
    }

    public string Name { get; }

    public string TableName { get; }

    public IReadOnlyList<string> ColumnNames => Columns;

    public ProjectionSubscription Subscription { get; }

    public string? OwnTopicPrefix { get; }

    public int MapCalls => Volatile.Read(ref _mapCalls);

    public string GroupId => $"projections.{Name}";

    public string CreateTableSql =>
        $"CREATE TABLE IF NOT EXISTS {TableName} (legal_entity_id UUID, aggregate_id UUID, name String, _event_id UUID, " +
        "_aggregate_version UInt64, _ingested_at DateTime64(3, 'UTC')) " +
        "ENGINE = ReplacingMergeTree(_aggregate_version) ORDER BY (legal_entity_id, aggregate_id)";

    public IReadOnlyList<object?[]> Map(ProjectionRecord record)
    {
        Interlocked.Increment(ref _mapCalls);
        if (OwnTopicPrefix is not null && !record.Topic.StartsWith(OwnTopicPrefix, StringComparison.Ordinal))
        {
            return [];
        }

        var payload = record.Payload ?? throw new InvalidOperationException("Test events always carry a payload.");
        var name = (string)payload["name"];
        if (name == "explode")
        {
            throw new InvalidOperationException("Test projection cannot map 'explode'.");
        }

        var e = record.Envelope;
        object version = name == "bad-type" ? "not-a-number" : checked((ulong)e.AggregateVersion);
        return [[e.LegalEntityId ?? Guid.Empty, e.AggregateId, name, e.EventId, version, record.IngestedAt]];
    }

    /// <summary>A unique, valid projection name (also the table name).</summary>
    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 13)];
}

/// <summary>Builds and produces test events.</summary>
internal static class TestEvents
{
    public static readonly Guid LegalEntity = Guid.Parse("0197a000-0000-7000-8000-0000000000e1");

    public static EventEnvelope Envelope(Guid aggregateId, long version = 1, Guid? eventId = null, DateTimeOffset? occurredAt = null) =>
        new(
            eventId ?? Guid.CreateVersion7(),
            "test.thing.created",
            "thing",
            aggregateId,
            version,
            occurredAt ?? DateTimeOffset.UtcNow,
            Guid.CreateVersion7(),
            1,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            LegalEntity);

    public static GenericRecord Payload(string name)
    {
        var record = new GenericRecord(TestProjection.Schema);
        record.Add("name", name);
        return record;
    }

    /// <summary>Produces one valid event; returns its envelope.</summary>
    public static async Task<EventEnvelope> ProduceAsync(KafkaFixture kafka, string topic, string name, Guid? aggregateId = null, long version = 1, EventEnvelope? envelope = null)
    {
        envelope ??= Envelope(aggregateId ?? Guid.CreateVersion7(), version);
        var bytes = await kafka.SerializeAsync(topic, Payload(name));
        await kafka.ProduceRawAsync(topic, envelope.AggregateId.ToString(), bytes, KafkaFixture.HeadersFor(envelope));
        return envelope;
    }

    public static Headers Headers(EventEnvelope envelope, params string[] omit) => KafkaFixture.HeadersFor(envelope, omit);
}

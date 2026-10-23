using System.Text.Json;
using Avro;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Persistence.Outbox;
using Shouldly;

namespace Nexus.BuildingBlocks.Persistence.Tests;

/// <summary>Pure unit tests of the IDomainEvent -> Avro JSON bridge (no database).</summary>
public sealed class AvroPayloadSerializerTests
{
    private static readonly RecordSchema Schema = (RecordSchema)Avro.Schema.Parse(
        """
        {"type":"record","name":"Probe","namespace":"ch.nussli.nexus.test","fields":[
          {"name":"id","type":{"type":"string","logicalType":"uuid"}},
          {"name":"amount","type":{"type":"bytes","logicalType":"decimal","precision":6,"scale":2}},
          {"name":"at","type":{"type":"long","logicalType":"timestamp-micros"}},
          {"name":"day","type":{"type":"int","logicalType":"date"}},
          {"name":"count","type":"long"},
          {"name":"tags","type":{"type":"array","items":"string"}},
          {"name":"maybe","type":["null",{"type":"string","logicalType":"uuid"}],"default":null},
          {"name":"child","type":{"type":"record","name":"Child","fields":[{"name":"label","type":"string"}]}},
          {"name":"legalEntityId","type":["null",{"type":"string","logicalType":"uuid"}],"default":null}
        ]}
        """);

    [Fact]
    public void ValidEvent_IsEncodedAsAvroJson_IncludingEnvelopeFieldDeclaredInSchema()
    {
        var le = Guid.NewGuid();
        var e = Valid() with { LegalEntityId = le, Maybe = Guid.Empty };

        using var json = JsonDocument.Parse(AvroPayloadSerializer.Serialize(e, Schema));
        var root = json.RootElement;
        root.GetProperty("id").GetString().ShouldBe(e.Id.ToString());
        root.GetProperty("count").GetInt64().ShouldBe(3);
        root.GetProperty("day").GetInt32().ShouldBe(20384); // 2025-10-23 in days since epoch
        root.GetProperty("maybe").GetProperty("string").GetString().ShouldBe(Guid.Empty.ToString());
        root.GetProperty("child").GetProperty("label").GetString().ShouldBe("c");
        root.GetProperty("legalEntityId").GetProperty("string").GetString().ShouldBe(le.ToString());
    }

    [Theory]
    [InlineData("12.345")] // more fractional digits than scale 2: never rounded
    [InlineData("10000.00")] // 7 digits > precision 6
    public void DecimalThatDoesNotFitExactly_IsRejected(string amount)
    {
        var e = Valid() with { Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) };
        Should.Throw<EventPayloadSchemaException>(() => AvroPayloadSerializer.Serialize(e, Schema));
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("-9999.99")]
    [InlineData("1.500000")] // trailing zeros beyond the scale are exact
    public void DecimalThatFits_IsAccepted(string amount)
    {
        var e = Valid() with { Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture) };
        Should.NotThrow(() => AvroPayloadSerializer.Serialize(e, Schema));
    }

    [Fact]
    public void NonUtcDateTime_IsRejected()
    {
        var e = new ProbeWithDateTime(Valid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        Should.Throw<EventPayloadSchemaException>(() => AvroPayloadSerializer.Serialize(e, Schema))
            .Message.ShouldContain("Kind=Unspecified");
    }

    [Fact]
    public void PublicPropertyNotInSchema_IsRejected()
    {
        var e = new ProbeWithExtra(Valid());
        Should.Throw<EventPayloadSchemaException>(() => AvroPayloadSerializer.Serialize(e, Schema))
            .Message.ShouldContain("Extra");
    }

    [Fact]
    public void NullInNonNullableField_IsRejected()
    {
        var e = Valid() with { Tags = null! };
        Should.Throw<EventPayloadSchemaException>(() => AvroPayloadSerializer.Serialize(e, Schema));
    }

    [Fact]
    public void WrongClrType_IsRejected()
    {
        var e = new ProbeWithStringId(Valid());
        Should.Throw<EventPayloadSchemaException>(() => AvroPayloadSerializer.Serialize(e, Schema));
    }

    private static Probe Valid() => new(
        Guid.NewGuid(),
        12.34m,
        DateTimeOffset.UtcNow,
        new DateOnly(2025, 10, 23),
        3,
        ["a", "b"],
        null,
        new ProbeChild("c"));

    public sealed record ProbeChild(string Label);

    public record Probe(Guid Id, decimal Amount, DateTimeOffset At, DateOnly Day, long Count, string[] Tags, Guid? Maybe, ProbeChild Child)
        : IDomainEvent
    {
        public string AggregateType => "probe";

        public Guid AggregateId => Id;

        public string EventType => "test.probe.happened";

        public int SchemaVersion => 1;

        public string Topic => "nexus.test.probe.happened";

        public Guid? LegalEntityId { get; init; }
    }

    private sealed class ProbeWithExtra(Probe p) : IDomainEvent
    {
        public Guid Id => p.Id;

        public decimal Amount => p.Amount;

        public DateTimeOffset At => p.At;

        public DateOnly Day => p.Day;

        public long Count => p.Count;

        public string[] Tags => p.Tags;

        public Guid? Maybe => p.Maybe;

        public ProbeChild Child => p.Child;

        public string Extra => p.Topic + " (not in schema)";

        public string AggregateType => p.AggregateType;

        public Guid AggregateId => p.AggregateId;

        public string EventType => p.EventType;

        public int SchemaVersion => p.SchemaVersion;

        public string Topic => p.Topic;

        public Guid? LegalEntityId => p.LegalEntityId;
    }

    private sealed class ProbeWithDateTime(Probe p, DateTime at) : IDomainEvent
    {
        public Guid Id => p.Id;

        public decimal Amount => p.Amount;

        public DateTime At => at;

        public DateOnly Day => p.Day;

        public long Count => p.Count;

        public string[] Tags => p.Tags;

        public Guid? Maybe => p.Maybe;

        public ProbeChild Child => p.Child;

        public string AggregateType => p.AggregateType;

        public Guid AggregateId => p.AggregateId;

        public string EventType => p.EventType;

        public int SchemaVersion => p.SchemaVersion;

        public string Topic => p.Topic;

        public Guid? LegalEntityId => p.LegalEntityId;
    }

    private sealed class ProbeWithStringId(Probe p) : IDomainEvent
    {
        public string Id => p.Id.ToString();

        public decimal Amount => p.Amount;

        public DateTimeOffset At => p.At;

        public DateOnly Day => p.Day;

        public long Count => p.Count;

        public string[] Tags => p.Tags;

        public Guid? Maybe => p.Maybe;

        public ProbeChild Child => p.Child;

        public string AggregateType => p.AggregateType;

        public Guid AggregateId => p.AggregateId;

        public string EventType => p.EventType;

        public int SchemaVersion => p.SchemaVersion;

        public string Topic => p.Topic;

        public Guid? LegalEntityId => p.LegalEntityId;
    }
}

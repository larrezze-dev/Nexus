using Avro;
using Avro.Generic;
using Nexus.BuildingBlocks.Messaging;
using Nexus.BuildingBlocks.Testing;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Projections;

namespace Nexus.Projections.Tests.Projections;

/// <summary>Builders shared by the projection tests: real org schemas from contracts/ and envelopes.</summary>
internal static class TestData
{
    public static readonly DateTime Ingested = new(2026, 11, 8, 12, 0, 0, 123, DateTimeKind.Utc);

    public static RecordSchema OrgSchema(string file) =>
        (RecordSchema)Schema.Parse(File.ReadAllText(RepoRoot.Find($"contracts/events/org/{file}")));

    /// <summary>Registry with the given (topic, pii) flags, built through the real parser.</summary>
    public static TopicRegistry Registry(params (string Topic, bool Pii)[] topics) =>
        TopicRegistry.Parse("topics:\n" + string.Concat(topics.Select(t =>
            $"  - name: {t.Topic}\n    pii: {(t.Pii ? "true" : "false")}\n")));

    public static EventEnvelope Envelope(string eventType, string aggregateType, Guid aggregateId, long version = 3,
        Guid? legalEntityId = null, Guid? actor = null, Guid? eventId = null) =>
        new(eventId ?? Guid.Parse("0197a000-0000-7000-8000-000000000001"), eventType, aggregateType, aggregateId, version,
            new DateTimeOffset(2026, 11, 8, 10, 30, 15, 123, TimeSpan.Zero).AddTicks(4560),
            actor, 1, Guid.Parse("0197a000-0000-7000-8000-0000000000c0"), Guid.Parse("0197a000-0000-7000-8000-0000000000ca"),
            legalEntityId);

    public static GenericRecord LegalEntity(string file, Guid id, bool isActive = true)
    {
        var r = new GenericRecord(OrgSchema(file));
        r.Add("id", id.ToString());
        r.Add("partyId", Guid.Parse("0197a000-0000-7000-8000-0000000000a1").ToString());
        r.Add("groupId", Guid.Parse("0197a000-0000-7000-8000-0000000000a2").ToString());
        r.Add("code", "NUS-CH");
        r.Add("currencyCode", "CHF");
        r.Add("countryCode", "CH");
        r.Add("isActive", isActive);
        return r;
    }

    public static GenericRecord Warehouse(string file, Guid id, Guid legalEntityId, Guid? businessUnitId, bool isActive = true)
    {
        var r = new GenericRecord(OrgSchema(file));
        r.Add("id", id.ToString());
        r.Add("locationId", Guid.Parse("0197a000-0000-7000-8000-0000000000b1").ToString());
        r.Add("legalEntityId", legalEntityId.ToString());
        r.Add("businessUnitId", businessUnitId?.ToString());
        r.Add("code", "WH-1");
        r.Add("captionI18n", new Dictionary<string, object> { ["en"] = "Main", ["de"] = "Haupt" });
        r.Add("isActive", isActive);
        return r;
    }

    public static GenericRecord BusinessUnit(Guid id, Guid legalEntityId)
    {
        var schema = OrgSchema("business-unit.created.v1.avsc");
        var r = new GenericRecord(schema);
        foreach (var f in schema.Fields)
        {
            r.Add(f.Name, f.Name switch
            {
                "id" => id.ToString(),
                "legalEntityId" => legalEntityId.ToString(),
                _ => SampleValue(f.Schema),
            });
        }
        return r;
    }

    private static object? SampleValue(Schema s) => s switch
    {
        UnionSchema => null,
        LogicalSchema l when l.LogicalTypeName == "uuid" => Guid.Empty.ToString(),
        _ when s.Tag == Schema.Type.Boolean => true,
        _ when s.Tag == Schema.Type.String => s.GetProperty("logicalType") == "uuid" ? Guid.Empty.ToString() : "x",
        _ when s.Tag == Schema.Type.Map => new Dictionary<string, object> { ["en"] = "x" },
        _ => throw new NotSupportedException(s.Tag.ToString()),
    };

    public static ProjectionRecord Record(string topic, EventEnvelope envelope, GenericRecord? payload) =>
        new(topic, envelope, payload, Ingested);
}

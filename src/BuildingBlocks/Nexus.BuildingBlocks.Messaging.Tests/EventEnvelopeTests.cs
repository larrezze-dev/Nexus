using System.Text;
using Confluent.Kafka;
using Shouldly;

namespace Nexus.BuildingBlocks.Messaging.Tests;

public class EventEnvelopeTests
{
    private static readonly Guid EventId = Guid.CreateVersion7();
    private static readonly Guid AggregateId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly Guid CorrelationId = Guid.CreateVersion7();
    private static readonly Guid CausationId = Guid.CreateVersion7();
    private static readonly Guid LegalEntityId = Guid.CreateVersion7();

    private static Headers Full(string? legalEntity = null)
    {
        var h = new Headers();
        Add(h, EventHeaderNames.EventId, EventId.ToString());
        Add(h, EventHeaderNames.EventType, "catalog.asset_type.created");
        Add(h, EventHeaderNames.AggregateType, "asset_type");
        Add(h, EventHeaderNames.AggregateId, AggregateId.ToString());
        Add(h, EventHeaderNames.AggregateVersion, "7");
        Add(h, EventHeaderNames.OccurredAt, "2026-11-07T10:15:30Z");
        Add(h, EventHeaderNames.ActorUserId, ActorId.ToString());
        Add(h, EventHeaderNames.SchemaVersion, "2");
        Add(h, EventHeaderNames.CorrelationId, CorrelationId.ToString());
        Add(h, EventHeaderNames.CausationId, CausationId.ToString());
        Add(h, EventHeaderNames.LegalEntityId, legalEntity ?? LegalEntityId.ToString());
        return h;
    }

    private static void Add(Headers h, string name, string value) =>
        h.Add(name, Encoding.UTF8.GetBytes(value));

    [Fact]
    public void FromHeaders_parses_all_headers()
    {
        var e = EventEnvelope.FromHeaders(Full());

        e.EventId.ShouldBe(EventId);
        e.EventType.ShouldBe("catalog.asset_type.created");
        e.AggregateType.ShouldBe("asset_type");
        e.AggregateId.ShouldBe(AggregateId);
        e.AggregateVersion.ShouldBe(7L);
        e.OccurredAt.ShouldBe(new DateTimeOffset(2026, 11, 7, 10, 15, 30, TimeSpan.Zero));
        e.ActorUserId.ShouldBe(ActorId);
        e.SchemaVersion.ShouldBe(2);
        e.CorrelationId.ShouldBe(CorrelationId);
        e.CausationId.ShouldBe(CausationId);
        e.LegalEntityId.ShouldBe(LegalEntityId);
    }

    [Fact]
    public void FromHeaders_empty_legal_entity_is_null()
    {
        EventEnvelope.FromHeaders(Full(legalEntity: "")).LegalEntityId.ShouldBeNull();
    }

    [Fact]
    public void FromHeaders_absent_legal_entity_and_actor_are_null()
    {
        var h = Full();
        h.Remove(EventHeaderNames.LegalEntityId);
        h.Remove(EventHeaderNames.ActorUserId);

        var e = EventEnvelope.FromHeaders(h);

        e.LegalEntityId.ShouldBeNull();
        e.ActorUserId.ShouldBeNull();
    }

    [Theory]
    [InlineData(EventHeaderNames.EventId)]
    [InlineData(EventHeaderNames.EventType)]
    [InlineData(EventHeaderNames.AggregateType)]
    [InlineData(EventHeaderNames.AggregateId)]
    [InlineData(EventHeaderNames.AggregateVersion)]
    [InlineData(EventHeaderNames.OccurredAt)]
    [InlineData(EventHeaderNames.SchemaVersion)]
    [InlineData(EventHeaderNames.CorrelationId)]
    [InlineData(EventHeaderNames.CausationId)]
    public void FromHeaders_missing_mandatory_header_throws(string missing)
    {
        var h = Full();
        h.Remove(missing);

        var ex = Should.Throw<InvalidEventEnvelopeException>(() => EventEnvelope.FromHeaders(h));
        ex.Message.ShouldContain(missing);
    }

    [Fact]
    public void FromHeaders_malformed_uuid_throws()
    {
        var h = Full();
        h.Remove(EventHeaderNames.EventId);
        Add(h, EventHeaderNames.EventId, "not-a-guid");

        Should.Throw<InvalidEventEnvelopeException>(() => EventEnvelope.FromHeaders(h));
    }
}

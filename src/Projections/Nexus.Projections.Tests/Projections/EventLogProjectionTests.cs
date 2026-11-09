using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.Projections.Projections;
using Shouldly;
using static Nexus.Projections.Tests.Projections.TestData;

namespace Nexus.Projections.Tests.Projections;

public sealed class EventLogProjectionTests
{
    private const string WhTopic = "nexus.org.warehouse.updated";
    private const string PartyTopic = "nexus.org.party.updated";
    private static readonly Guid Le = Guid.Parse("0197a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Wh = Guid.Parse("0197a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Actor = Guid.Parse("0197a000-0000-7000-8000-0000000000f1");

    private static EventLogProjection Create(ILogger<EventLogProjection>? logger = null) =>
        new(Registry((WhTopic, false), (PartyTopic, true), ("nexus.org.legal-entity.created", false),
                ("nexus.org.business-unit.created", false)),
            logger ?? NullLogger<EventLogProjection>.Instance);

    private static object?[] Single(EventLogProjection p, Nexus.Projections.Abstractions.ProjectionRecord r)
    {
        var rows = p.Map(r);
        rows.Count.ShouldBe(1);
        rows[0].Length.ShouldBe(p.ColumnNames.Count);
        return rows[0];
    }

    private static object? Col(EventLogProjection p, object?[] row, string name) => row[p.ColumnNames.ToList().IndexOf(name)];

    [Fact(DisplayName = "Producer aggregate-type literals are pinned: legal-entity, business_unit, warehouse")]
    public void Aggregate_literals_match_producer()
    {
        EventLogProjection.LegalEntityAggregate.ShouldBe("legal-entity");
        EventLogProjection.BusinessUnitAggregate.ShouldBe("business_unit");
        EventLogProjection.WarehouseAggregate.ShouldBe("warehouse");

        var domain = Directory.GetFiles(Nexus.BuildingBlocks.Testing.RepoRoot.Find("src/Platform/Nexus.Platform.Domain"), "*Events.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText).ToList();
        foreach (var literal in new[] { "legal-entity", "business_unit", "warehouse" })
            domain.ShouldContain(t => t.Contains($"AggregateType => \"{literal}\""));
    }

    [Fact(DisplayName = "Map: full event row for a non-PII warehouse event (headers + payload json)")]
    public void Maps_non_pii_event()
    {
        var p = Create();
        var env = Envelope("org.warehouse.updated", "warehouse", Wh, version: 7, actor: Actor);
        var row = Single(p, Record(WhTopic, env, Warehouse("warehouse.updated.v1.avsc", Wh, Le, null)));

        Col(p, row, "legal_entity_id").ShouldBe(Le);
        Col(p, row, "topic").ShouldBe(WhTopic);
        Col(p, row, "event_id").ShouldBe(env.EventId);
        Col(p, row, "event_type").ShouldBe("org.warehouse.updated");
        Col(p, row, "aggregate_type").ShouldBe("warehouse");
        Col(p, row, "aggregate_id").ShouldBe(Wh);
        Col(p, row, "aggregate_version").ShouldBe(7UL);
        var occurred = (DateTime)Col(p, row, "occurred_at")!;
        occurred.Kind.ShouldBe(DateTimeKind.Utc);
        occurred.Ticks.ShouldBe(new DateTime(2026, 11, 8, 10, 30, 15, 123, DateTimeKind.Utc).AddTicks(4560).Ticks); // microseconds kept
        Col(p, row, "actor_user_id").ShouldBe(Actor);
        Col(p, row, "correlation_id").ShouldBe(env.CorrelationId);
        Col(p, row, "_ingested_at").ShouldBe(Ingested);
        var json = (string)Col(p, row, "payload_json")!;
        json.ShouldStartWith("{\"id\":\"" + Wh);
        json.ShouldContain("\"captionI18n\":{\"de\":\"Haupt\",\"en\":\"Main\"}");
        json.ShouldContain("\"businessUnitId\":null");
    }

    [Fact(DisplayName = "Map: absent actor header is stored as Guid.Empty (column is not nullable)")]
    public void Absent_actor_is_empty_guid()
    {
        var p = Create();
        var row = Single(p, Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh, actor: null),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, null)));
        Col(p, row, "actor_user_id").ShouldBe(Guid.Empty);
    }

    [Fact(DisplayName = "Map: legal_entity_id of a legal-entity event is its aggregate id")]
    public void Le_from_aggregate_id()
    {
        var p = Create();
        var row = Single(p, Record("nexus.org.legal-entity.created", Envelope("org.legal-entity.created", "legal-entity", Le),
            LegalEntity("legal-entity.created.v1.avsc", Le)));
        Col(p, row, "legal_entity_id").ShouldBe(Le);
    }

    [Fact(DisplayName = "Map: legal_entity_id of business_unit and warehouse events comes from payload legalEntityId")]
    public void Le_from_payload()
    {
        var p = Create();
        var bu = Guid.Parse("0197a000-0000-7000-8000-0000000000d2");
        var row = Single(p, Record("nexus.org.business-unit.created", Envelope("org.business-unit.created", "business_unit", bu),
            BusinessUnit(bu, Le)));
        Col(p, row, "legal_entity_id").ShouldBe(Le);
        row = Single(p, Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, null)));
        Col(p, row, "legal_entity_id").ShouldBe(Le);
    }

    [Fact(DisplayName = "Map: header nexus-legal-entity-id takes precedence over derivation")]
    public void Header_precedence()
    {
        var p = Create();
        var other = Guid.Parse("0197a000-0000-7000-8000-0000000000e2");
        var row = Single(p, Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh, legalEntityId: other),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, null)));
        Col(p, row, "legal_entity_id").ShouldBe(other);
    }

    [Fact(DisplayName = "Map: other aggregate types have a null legal_entity_id")]
    public void Other_aggregates_null()
    {
        var p = Create();
        var party = Guid.Parse("0197a000-0000-7000-8000-0000000000aa");
        var row = Single(p, Record(PartyTopic, Envelope("org.party.updated", "party", party), null));
        Col(p, row, "legal_entity_id").ShouldBeNull();
    }

    [Fact(DisplayName = "Map: warehouse payload without a valid legalEntityId is a contract violation (throws)")]
    public void Missing_payload_le_throws()
    {
        var p = Create();
        var payload = Warehouse("warehouse.updated.v1.avsc", Wh, Le, null);
        payload.Add("legalEntityId", "not-a-guid");
        Should.Throw<InvalidOperationException>(() => p.Map(Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh), payload)));
        Should.Throw<InvalidOperationException>(() => p.Map(Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh), null)));
    }

    [Fact(DisplayName = "Map: negative aggregate version is rejected")]
    public void Negative_version_throws() =>
        Should.Throw<InvalidOperationException>(() => Create().Map(Record(PartyTopic, Envelope("org.party.updated", "party", Wh, version: -1), null)));

    [Fact(DisplayName = "EV-R5: PII topic stores an empty payload, headers kept, legal_entity_id still derived from the payload")]
    public void Pii_drops_payload_keeps_le()
    {
        // Same warehouse event, but the registry flags its topic as PII.
        var p = new EventLogProjection(Registry((WhTopic, true)), NullLogger<EventLogProjection>.Instance);
        var row = Single(p, Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh, actor: Actor),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, null)));
        Col(p, row, "payload_json").ShouldBe("");
        Col(p, row, "legal_entity_id").ShouldBe(Le);
        Col(p, row, "event_type").ShouldBe("org.warehouse.updated");
        Col(p, row, "actor_user_id").ShouldBe(Actor);
    }

    [Fact(DisplayName = "EV-R5: unknown topic fails closed (empty payload) and logs a single warning per topic, never the payload")]
    public void Unknown_topic_fail_closed_warns_once()
    {
        var logger = new CapturingLogger();
        var p = new EventLogProjection(Registry((PartyTopic, true)), logger);
        var payload = Warehouse("warehouse.updated.v1.avsc", Wh, Le, null);
        for (var i = 0; i < 3; i++)
        {
            var row = Single(p, Record("nexus.org.warehouse.updated", Envelope("org.warehouse.updated", "warehouse", Wh), payload));
            Col(p, row, "payload_json").ShouldBe("");
        }
        logger.Messages.Count.ShouldBe(1);
        logger.Messages[0].ShouldContain("nexus.org.warehouse.updated");
        logger.Messages[0].ShouldNotContain("WH-1");
    }

    [Fact(DisplayName = "Map: tombstone (null payload) on a non-PII topic stores an empty payload")]
    public void Tombstone_empty_payload()
    {
        var p = Create();
        var row = Single(p, Record("nexus.org.legal-entity.created", Envelope("org.legal-entity.created", "legal-entity", Le), null));
        Col(p, row, "payload_json").ShouldBe("");
        Col(p, row, "legal_entity_id").ShouldBe(Le);
    }

    [Fact(DisplayName = "Map is deterministic: the same record twice yields identical rows")]
    public void Deterministic()
    {
        var p = Create();
        var r = Record(WhTopic, Envelope("org.warehouse.updated", "warehouse", Wh), Warehouse("warehouse.updated.v1.avsc", Wh, Le, null));
        Single(p, r).ShouldBe(Single(p, r));
    }

    [Fact(DisplayName = "Subscription: three lowercase segments after nexus. (excludes .DLQ topics)")]
    public void Subscription_pattern()
    {
        var rx = new System.Text.RegularExpressions.Regex(Create().Subscription.Pattern!);
        rx.IsMatch("nexus.org.warehouse.updated").ShouldBeTrue();
        rx.IsMatch("nexus.stock.movement.posted").ShouldBeTrue();
        rx.IsMatch("nexus.org.warehouse.updated.DLQ").ShouldBeFalse();
        rx.IsMatch("nexus.org.warehouse").ShouldBeFalse();
        rx.IsMatch("other.org.warehouse.updated").ShouldBeFalse();
    }

    private sealed class CapturingLogger : ILogger<EventLogProjection>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Messages.Add(formatter(state, exception));
        }
    }
}

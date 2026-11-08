using Nexus.Projections.Projections;
using Shouldly;
using static Nexus.Projections.Tests.Projections.TestData;

namespace Nexus.Projections.Tests.Projections;

public sealed class DimProjectionsTests
{
    private static readonly Guid Le = Guid.Parse("0197a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Wh = Guid.Parse("0197a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Bu = Guid.Parse("0197a000-0000-7000-8000-0000000000d2");

    [Theory(DisplayName = "EV-R4: every legal-entity event carries full state and maps to one full dim_legal_entity row")]
    [InlineData("created", true)]
    [InlineData("updated", true)]
    [InlineData("deactivated", false)]
    public void Legal_entity_row(string verb, bool active)
    {
        var p = new DimLegalEntityProjection();
        var env = Envelope($"org.legal-entity.{verb}", "legal-entity", Le, version: 4);
        var rows = p.Map(Record($"nexus.org.legal-entity.{verb}", env, LegalEntity($"legal-entity.{verb}.v1.avsc", Le, active)));

        p.Subscription.Topics.ShouldBe([
            "nexus.org.legal-entity.created", "nexus.org.legal-entity.updated", "nexus.org.legal-entity.deactivated"]);
        rows.Count.ShouldBe(1);
        var row = rows[0];
        row.Length.ShouldBe(p.ColumnNames.Count);
        var cols = p.ColumnNames.ToList();
        row[cols.IndexOf("legal_entity_id")].ShouldBe(Le);
        row[cols.IndexOf("party_id")].ShouldBe(Guid.Parse("0197a000-0000-7000-8000-0000000000a1"));
        row[cols.IndexOf("group_id")].ShouldBe(Guid.Parse("0197a000-0000-7000-8000-0000000000a2"));
        row[cols.IndexOf("code")].ShouldBe("NUS-CH");
        row[cols.IndexOf("currency_code")].ShouldBe("CHF");
        row[cols.IndexOf("country_code")].ShouldBe("CH");
        row[cols.IndexOf("is_active")].ShouldBe(active);
        row[cols.IndexOf("_event_id")].ShouldBe(env.EventId);
        row[cols.IndexOf("_aggregate_version")].ShouldBe(4UL);
        row[cols.IndexOf("_ingested_at")].ShouldBe(Ingested);
    }

    [Theory(DisplayName = "EV-R4: every warehouse event carries full state and maps to one full dim_warehouse row")]
    [InlineData("created", true, false)]
    [InlineData("updated", true, true)]
    [InlineData("deactivated", false, true)]
    public void Warehouse_row(string verb, bool active, bool withBu)
    {
        var p = new DimWarehouseProjection();
        var env = Envelope($"org.warehouse.{verb}", "warehouse", Wh, version: 2);
        var rows = p.Map(Record($"nexus.org.warehouse.{verb}", env, Warehouse($"warehouse.{verb}.v1.avsc", Wh, Le, withBu ? Bu : null, active)));

        p.Subscription.Topics.ShouldBe([
            "nexus.org.warehouse.created", "nexus.org.warehouse.updated", "nexus.org.warehouse.deactivated"]);
        rows.Count.ShouldBe(1);
        var row = rows[0];
        row.Length.ShouldBe(p.ColumnNames.Count);
        var cols = p.ColumnNames.ToList();
        row[cols.IndexOf("legal_entity_id")].ShouldBe(Le);
        row[cols.IndexOf("warehouse_id")].ShouldBe(Wh);
        row[cols.IndexOf("location_id")].ShouldBe(Guid.Parse("0197a000-0000-7000-8000-0000000000b1"));
        row[cols.IndexOf("business_unit_id")].ShouldBe(withBu ? Bu : null);
        row[cols.IndexOf("code")].ShouldBe("WH-1");
        row[cols.IndexOf("caption_i18n")].ShouldBeOfType<Dictionary<string, string>>()
            .ShouldBe(new Dictionary<string, string> { ["en"] = "Main", ["de"] = "Haupt" });
        row[cols.IndexOf("is_active")].ShouldBe(active);
        row[cols.IndexOf("_event_id")].ShouldBe(env.EventId);
        row[cols.IndexOf("_aggregate_version")].ShouldBe(2UL);
        row[cols.IndexOf("_ingested_at")].ShouldBe(Ingested);
    }

    [Fact(DisplayName = "Dim projections: tombstone or missing field throws (contract violation, goes to DLQ)")]
    public void Invalid_throws()
    {
        var env = Envelope("org.warehouse.updated", "warehouse", Wh);
        Should.Throw<InvalidOperationException>(() => new DimWarehouseProjection().Map(Record("nexus.org.warehouse.updated", env, null)));
        var le = LegalEntity("legal-entity.updated.v1.avsc", Le);
        le.Add("code", null);
        Should.Throw<InvalidOperationException>(() => new DimLegalEntityProjection().Map(Record("nexus.org.legal-entity.updated", env, le)));
    }

    [Fact(DisplayName = "Dim projections are deterministic: the same record twice yields identical rows")]
    public void Deterministic()
    {
        var p = new DimWarehouseProjection();
        var r = Record("nexus.org.warehouse.updated", Envelope("org.warehouse.updated", "warehouse", Wh),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, Bu));
        var a = p.Map(r)[0];
        var b = p.Map(r)[0];
        a.Length.ShouldBe(b.Length);
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] is Dictionary<string, string> da) da.ShouldBe((Dictionary<string, string>)b[i]!);
            else a[i].ShouldBe(b[i]);
        }
    }

    [Fact(DisplayName = "AddNexusProjectionDefinitions registers the three projections")]
    public void Registration()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        services.AddNexusProjectionDefinitions(Nexus.BuildingBlocks.Testing.RepoRoot.Find("contracts/events/topics.yaml"));
        using var sp = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetServices<Nexus.Projections.Abstractions.IProjection>(sp).Select(x => x.Name).Order()
            .ShouldBe(["dim_legal_entity", "dim_warehouse", "event_log"]);
    }
}

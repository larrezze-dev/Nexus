using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.BuildingBlocks.Testing;
using Nexus.Projections.Abstractions;
using Nexus.Projections.Projections;
using Shouldly;
using Testcontainers.ClickHouse;
using static Nexus.Projections.Tests.Projections.TestData;

namespace Nexus.Projections.Tests.Projections;

/// <summary>
/// Applies the real db/clickhouse base migration to a throwaway ClickHouse and inserts the rows the
/// projections produce with the same bulk-copy path the runtime uses, proving the CLR types match the DDL.
/// </summary>
public sealed class ProjectionsClickHouseTests : IAsyncLifetime
{
    private static readonly Guid Le = Guid.Parse("0197a000-0000-7000-8000-0000000000e1");
    private static readonly Guid Wh = Guid.Parse("0197a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Bu = Guid.Parse("0197a000-0000-7000-8000-0000000000d2");

    private readonly ClickHouseContainer _container = new ClickHouseBuilder("clickhouse/clickhouse-server:26.8.34.13").Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var conn = Connect();
        await conn.OpenAsync();
        var sql = File.ReadAllText(RepoRoot.Find("db/clickhouse/V202611082100__base.sql"));
        foreach (var fragment in sql.Split(';'))
        {
            var statement = string.Join('\n', fragment.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
            if (string.IsNullOrWhiteSpace(statement)) continue;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = statement;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private ClickHouseConnection Connect() => new(_container.GetConnectionString());

    private async Task Insert(IProjection projection, params ProjectionRecord[] records)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        using var copy = new ClickHouseBulkCopy(conn)
        {
            DestinationTableName = projection.TableName,
            ColumnNames = projection.ColumnNames,
        };
        await copy.InitAsync();
        await copy.WriteToServerAsync(records.SelectMany(projection.Map).Select(r => r).ToList());
    }

    private async Task<T> Scalar<T>(string sql)
    {
        await using var conn = Connect();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact(DisplayName = "event_log: rows insert with CLR types matching the DDL; the same event twice is one row with FINAL")]
    public async Task Event_log_roundtrip()
    {
        var registry = Registry(("nexus.org.warehouse.updated", false), ("nexus.org.party.updated", true));
        var p = new EventLogProjection(registry, NullLogger<EventLogProjection>.Instance);
        var wh = Record("nexus.org.warehouse.updated", Envelope("org.warehouse.updated", "warehouse", Wh, version: 5),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, null));
        var party = Guid.Parse("0197a000-0000-7000-8000-0000000000aa");
        var pii = Record("nexus.org.party.updated", Envelope("org.party.updated", "party", party, eventId: Guid.Parse("0197a000-0000-7000-8000-000000000002")), null);

        await Insert(p, wh, wh, pii);
        await Insert(p, wh); // a later redelivery

        (await Scalar<long>($"SELECT count() FROM nexus_analytics.event_log FINAL WHERE aggregate_id = '{Wh}'")).ShouldBe(1);
        (await Scalar<string>($"SELECT payload_json FROM nexus_analytics.event_log FINAL WHERE aggregate_id = '{Wh}'")).ShouldContain("\"code\":\"WH-1\"");
        (await Scalar<long>($"SELECT count() FROM nexus_analytics.event_log FINAL WHERE aggregate_id = '{party}' AND payload_json = '' AND legal_entity_id IS NULL AND actor_user_id = toUUID('00000000-0000-0000-0000-000000000000')")).ShouldBe(1);
        (await Scalar<long>($"SELECT count() FROM nexus_analytics.event_log FINAL WHERE aggregate_id = '{Wh}' AND legal_entity_id = '{Le}' AND aggregate_version = 5 AND toString(occurred_at) = '2026-11-08 10:30:15.123456'")).ShouldBe(1);
    }

    [Fact(DisplayName = "dim_legal_entity: row types match the DDL; replay is one row, the highest aggregate version wins")]
    public async Task Dim_legal_entity_roundtrip()
    {
        var p = new DimLegalEntityProjection();
        var created = Record("nexus.org.legal-entity.created", Envelope("org.legal-entity.created", "legal-entity", Le, version: 1),
            LegalEntity("legal-entity.created.v1.avsc", Le));
        var deactivated = Record("nexus.org.legal-entity.deactivated", Envelope("org.legal-entity.deactivated", "legal-entity", Le, version: 2, eventId: Guid.Parse("0197a000-0000-7000-8000-000000000003")),
            LegalEntity("legal-entity.deactivated.v1.avsc", Le, isActive: false));

        // Out of order and duplicated on purpose.
        await Insert(p, deactivated, created, created);

        (await Scalar<long>($"SELECT count() FROM nexus_analytics.dim_legal_entity FINAL WHERE legal_entity_id = '{Le}'")).ShouldBe(1);
        (await Scalar<long>($"SELECT count() FROM nexus_analytics.dim_legal_entity FINAL WHERE legal_entity_id = '{Le}' AND is_active = false AND _aggregate_version = 2 AND currency_code = 'CHF'")).ShouldBe(1);
    }

    [Fact(DisplayName = "dim_warehouse: row types match the DDL (nullable business unit, Map caption); replay is one row")]
    public async Task Dim_warehouse_roundtrip()
    {
        var p = new DimWarehouseProjection();
        var created = Record("nexus.org.warehouse.created", Envelope("org.warehouse.created", "warehouse", Wh, version: 1),
            Warehouse("warehouse.created.v1.avsc", Wh, Le, null));
        var updated = Record("nexus.org.warehouse.updated", Envelope("org.warehouse.updated", "warehouse", Wh, version: 2, eventId: Guid.Parse("0197a000-0000-7000-8000-000000000004")),
            Warehouse("warehouse.updated.v1.avsc", Wh, Le, Bu));

        await Insert(p, created, updated, updated);

        (await Scalar<long>($"SELECT count() FROM nexus_analytics.dim_warehouse FINAL WHERE legal_entity_id = '{Le}' AND warehouse_id = '{Wh}'")).ShouldBe(1);
        (await Scalar<long>($"SELECT count() FROM nexus_analytics.dim_warehouse FINAL WHERE warehouse_id = '{Wh}' AND business_unit_id = '{Bu}' AND caption_i18n['de'] = 'Haupt' AND is_active")).ShouldBe(1);
    }
}

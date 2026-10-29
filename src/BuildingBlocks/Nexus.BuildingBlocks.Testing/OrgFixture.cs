using Npgsql;
using Xunit;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>
/// The standard organisational test data of conventions-testing.md §4, on a real, fully
/// migrated PostgreSQL (<see cref="Database"/>, a <see cref="PostgresFixture"/> it owns):
/// <list type="bullet">
///   <item>The common seed (<c>db/seed/common/V*.sql</c>, the same files <c>make seed-dev</c>
///   applies): currencies and the SYSTEM user.</item>
///   <item>One group, two legal entities (<see cref="LegalEntityA"/> in CHF/CH,
///   <see cref="LegalEntityB"/> in EUR/DE), one business unit, one location and one warehouse
///   in each (<see cref="WarehouseA"/>, <see cref="WarehouseB"/>).</item>
///   <item>Three users, each with its <c>PERSON</c> party and a fixed <c>oid</c>
///   (<c>external_subject</c>) a test can put in a JWT (<see cref="TestJwtIssuer"/>):
///   <see cref="Admin"/> (meant as GLOBAL scope), <see cref="UserA"/> (LE-A) and
///   <see cref="WhA"/> (only WH-A). The scopes themselves are granted from step 0.6 on, when
///   <c>access</c> exists; until then the three are plain active users.</item>
///   <item><see cref="UnprovisionedOid"/>: an <c>oid</c> with no <c>app_user</c>.</item>
/// </list>
/// Ids are fresh UUIDv7s per fixture instance; codes and oids are fixed (each fixture has its
/// own database). All rows are written as the superuser, attributed to SYSTEM.
/// <para>
/// Use it as a class fixture (one clean database per test class, conventions-testing.md §4).
/// Tests must not modify these rows; they create their own data next to them.
/// </para>
/// </summary>
public sealed class OrgFixture : IAsyncLifetime
{
    /// <summary>The SYSTEM user (and its party), seeded by <c>db/seed/common</c>.</summary>
    public static readonly Guid SystemUserId = new("00000000-0000-7000-8000-000000000001");

    /// <summary>An <c>oid</c> that has no <c>app_user</c> (403 <c>user_not_provisioned</c>).</summary>
    public const string UnprovisionedOid = "00000000-0000-7000-8000-00000000f0ff";

    public PostgresFixture Database { get; } = new();

    public Guid GroupId { get; } = NewId();

    public OrgUnit LegalEntityA { get; } = new(NewId(), NewId(), "LE-A");

    public OrgUnit LegalEntityB { get; } = new(NewId(), NewId(), "LE-B");

    public OrgUnit BusinessUnitA { get; } = new(NewId(), NewId(), "BU-A");

    public OrgUnit BusinessUnitB { get; } = new(NewId(), NewId(), "BU-B");

    public Guid LocationA { get; } = NewId();

    public Guid LocationB { get; } = NewId();

    public Guid WarehouseA { get; } = NewId();

    public Guid WarehouseB { get; } = NewId();

    public OrgTestUser Admin { get; } = new(NewId(), NewId(), "00000000-0000-7000-8000-00000000f001", "admin@test.nexus.local", "Admin Test");

    public OrgTestUser UserA { get; } = new(NewId(), NewId(), "00000000-0000-7000-8000-00000000f002", "usera@test.nexus.local", "User A Test");

    public OrgTestUser WhA { get; } = new(NewId(), NewId(), "00000000-0000-7000-8000-00000000f004", "wha@test.nexus.local", "Warehouse A Test");

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        await ApplyCommonSeedAsync();
        await SeedStructureAsync();
    }

    public ValueTask DisposeAsync() => Database.DisposeAsync();

    private static Guid NewId() => Guid.CreateVersion7();

    /// <summary>Runs <c>db/seed/common/V*.sql</c> in version order, as Flyway would.</summary>
    private async Task ApplyCommonSeedAsync()
    {
        var directory = RepoRoot.Find("db/seed/common");
        foreach (var file in Directory.GetFiles(directory, "V*.sql").Order(StringComparer.Ordinal))
        {
            await using var command = Database.Admin.CreateCommand(await File.ReadAllTextAsync(file));
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task SeedStructureAsync()
    {
        await using var connection = await Database.Admin.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        async Task Exec(string sql, params object?[] values)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            foreach (var value in values)
            {
                command.Parameters.Add(new NpgsqlParameter { Value = value ?? DBNull.Value });
            }

            await command.ExecuteNonQueryAsync();
        }

        const string Std = "now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'";
        const string PartySql =
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, $3, true, true, " + Std + ")";

        var groupPartyId = NewId();
        await Exec(PartySql, groupPartyId, "ORGANIZATION", "Test Group");
        await Exec(
            "INSERT INTO org.\"group\" (id, party_id, code, reporting_currency_code, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, 'TEST-GROUP', 'CHF', " + Std + ")",
            GroupId,
            groupPartyId);

        foreach (var (le, currency, country) in new[] { (LegalEntityA, "CHF", "CH"), (LegalEntityB, "EUR", "DE") })
        {
            await Exec(PartySql, le.PartyId, "ORGANIZATION", "Test " + le.Code);
            await Exec(
                "INSERT INTO org.legal_entity (id, party_id, group_id, code, currency_code, country_code, is_active, created_at, created_by, updated_at, updated_by) " +
                "VALUES ($1, $2, $3, $4, $5, $6, true, " + Std + ")",
                le.Id,
                le.PartyId,
                GroupId,
                le.Code,
                currency,
                country);
        }

        foreach (var (bu, le) in new[] { (BusinessUnitA, LegalEntityA), (BusinessUnitB, LegalEntityB) })
        {
            await Exec(PartySql, bu.PartyId, "ORGANIZATION", "Test " + bu.Code);
            await Exec(
                "INSERT INTO org.business_unit (id, party_id, legal_entity_id, code, caption_i18n, is_active, created_at, created_by, updated_at, updated_by) " +
                "VALUES ($1, $2, $3, $4, jsonb_build_object('de', $4::text, 'en', $4::text), true, " + Std + ")",
                bu.Id,
                bu.PartyId,
                le.Id,
                bu.Code);
        }

        foreach (var (location, warehouse, le, bu, suffix, country) in new[]
                 {
                     (LocationA, WarehouseA, LegalEntityA, BusinessUnitA, "A", "CH"),
                     (LocationB, WarehouseB, LegalEntityB, BusinessUnitB, "B", "DE"),
                 })
        {
            await Exec(
                "INSERT INTO org.location (id, code, name, country_code, is_active, created_at, created_by, updated_at, updated_by) " +
                "VALUES ($1, $2, $3, $4, true, " + Std + ")",
                location,
                "LOC-" + suffix,
                "Test location " + suffix,
                country);
            await Exec(
                "INSERT INTO org.warehouse (id, location_id, legal_entity_id, business_unit_id, code, caption_i18n, is_active, created_at, created_by, updated_at, updated_by) " +
                "VALUES ($1, $2, $3, $4, $5, jsonb_build_object('de', $5::text, 'en', $5::text), true, " + Std + ")",
                warehouse,
                location,
                le.Id,
                bu.Id,
                "WH-" + suffix);
        }

        foreach (var (user, businessUnit) in new[] { (Admin, (Guid?)null), (UserA, BusinessUnitA.Id), (WhA, BusinessUnitA.Id) })
        {
            await Exec(PartySql, user.PartyId, "PERSON", user.FullName);
            await Exec(
                "INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
                "VALUES ($1, $2, $3, $4, $5, 'de', true, " + Std + ")",
                user.Id,
                user.PartyId,
                businessUnit,
                user.Oid,
                user.Email);
        }

        await transaction.CommitAsync();
    }
}

/// <summary>A group-internal organisational unit seeded by <see cref="OrgFixture"/> (legal entity or business unit).</summary>
public sealed record OrgUnit(Guid Id, Guid PartyId, string Code);

/// <summary>A user seeded by <see cref="OrgFixture"/>: <c>app_user</c> + its <c>PERSON</c> party; language <c>de</c>.</summary>
public sealed record OrgTestUser(Guid Id, Guid PartyId, string Oid, string Email, string FullName);

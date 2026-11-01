using Npgsql;
using NpgsqlTypes;
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
///   <item>Four users, each with its <c>PERSON</c> party and a fixed <c>oid</c>
///   (<c>external_subject</c>) a test can put in a JWT (<see cref="TestJwtIssuer"/>):
///   <see cref="Admin"/> (GLOBAL), <see cref="UserA"/> (LE-A), <see cref="UserB"/> (LE-B)
///   and <see cref="WhA"/> (only WH-A). Since step 0.6, each also gets the matching
///   <c>access.role_assignment</c> (<c>SYSTEM_ADMIN</c>/<c>ORG_ADMIN</c>/<c>ORG_ADMIN</c>/
///   <c>WAREHOUSE_OPERATOR</c>), resolved by role <c>code</c> against the roles
///   <c>db/seed/common</c> seeds (applied by <see cref="ApplyCommonSeedAsync"/> above).</item>
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

    public OrgTestUser UserB { get; } = new(NewId(), NewId(), "00000000-0000-7000-8000-00000000f003", "userb@test.nexus.local", "User B Test");

    public OrgTestUser WhA { get; } = new(NewId(), NewId(), "00000000-0000-7000-8000-00000000f004", "wha@test.nexus.local", "Warehouse A Test");

    public async ValueTask InitializeAsync()
    {
        await Database.InitializeAsync();
        await ApplyCommonSeedAsync();
        await SeedStructureAsync();
    }

    public ValueTask DisposeAsync() => Database.DisposeAsync();

    /// <summary>
    /// Runs <paramref name="action"/> against an owner connection (<see
    /// cref="PostgresFixture.OpenAsOwnerAsync"/> -- needed for tables such as
    /// <c>test.rls_probe</c> that have no <c>GRANT</c> to any <c>svc_*</c> role at all) with
    /// <c>app.current_user_id</c> <c>SET LOCAL</c>'d to <paramref name="userId"/>, so
    /// <c>access</c>'s <c>visible_*</c> functions and any RLS policy built on them resolve as
    /// that user would see them. Always <c>ROLLBACK</c>s afterwards (read-only test helper:
    /// never commits, so test data never leaks between tests), then disposes the connection.
    /// </summary>
    public async Task<T> AsUser<T>(Guid userId, Func<NpgsqlConnection, Task<T>> action)
    {
        await using var connection = await Database.OpenAsOwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var setConfig = new NpgsqlCommand("SELECT set_config('app.current_user_id', $1, true)", connection))
        {
            setConfig.Parameters.Add(new NpgsqlParameter { Value = userId.ToString("D") });
            await setConfig.ExecuteNonQueryAsync();
        }

        var result = await action(connection);
        await transaction.RollbackAsync();
        return result;
    }

    /// <summary>Overload of <see cref="AsUser{T}"/> for actions with no return value.</summary>
    public Task AsUser(Guid userId, Func<NpgsqlConnection, Task> action) =>
        AsUser<object?>(userId, async connection =>
        {
            await action(connection);
            return null;
        });

    /// <summary>
    /// Like <see cref="AsUser{T}"/>, but with no <c>app.current_user_id</c> set at all (the
    /// "anonymous"/no-session case some RLS policies also need covered). Same owner connection,
    /// same always-<c>ROLLBACK</c> contract.
    /// </summary>
    public async Task<T> AsNoUser<T>(Func<NpgsqlConnection, Task<T>> action)
    {
        await using var connection = await Database.OpenAsOwnerAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var result = await action(connection);
        await transaction.RollbackAsync();
        return result;
    }

    /// <summary>Overload of <see cref="AsNoUser{T}"/> for actions with no return value.</summary>
    public Task AsNoUser(Func<NpgsqlConnection, Task> action) =>
        AsNoUser<object?>(async connection =>
        {
            await action(connection);
            return null;
        });

    /// <summary>
    /// Bulk-inserts <paramref name="count"/> rows into <c>access.role_assignment</c> via a binary
    /// <c>COPY</c> (as the owner -- these are seed inserts, not an RLS-filtered read, so no
    /// <c>app.current_user_id</c> is needed), for the ADR-0004 benchmark's two shapes:
    /// <list type="bullet">
    ///   <item>Pathological: one user, <paramref name="count"/> assignments
    ///   (<c>userIdFactory: _ => sameUserId</c>).</item>
    ///   <item>Realistic: <paramref name="count"/> assignments spread one per user
    ///   (<c>userIdFactory: i => someExistingUserId[i]</c>; the caller is responsible for those
    ///   users already existing in <c>org.app_user</c>, the FK <c>role_assignment</c> requires).</item>
    /// </list>
    /// <paramref name="scopeEntityIdFactory"/> must return <c>null</c> for every row when
    /// <paramref name="scopeType"/> is <c>GLOBAL</c> (the only case the DDL's
    /// <c>(scope_type = 'GLOBAL') = (scope_entity_id IS NULL)</c> check allows).
    /// </summary>
    public async Task SeedManyRoleAssignmentsAsync(
        Func<int, Guid> userIdFactory,
        Guid roleId,
        string scopeType,
        Func<int, Guid?> scopeEntityIdFactory,
        int count)
    {
        await using var connection = await Database.OpenAsOwnerAsync();
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY access.role_assignment " +
            "(id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "FROM STDIN (FORMAT BINARY)");

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < count; i++)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(NewId(), NpgsqlDbType.Uuid); // id
            await writer.WriteAsync(userIdFactory(i), NpgsqlDbType.Uuid); // user_id
            await writer.WriteAsync(roleId, NpgsqlDbType.Uuid); // role_id
            await writer.WriteAsync(scopeType, NpgsqlDbType.Text); // scope_type

            var scopeEntityId = scopeEntityIdFactory(i);
            if (scopeEntityId is { } id)
            {
                await writer.WriteAsync(id, NpgsqlDbType.Uuid);
            }
            else
            {
                await writer.WriteNullAsync();
            }

            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz); // valid_from
            await writer.WriteNullAsync(); // valid_to
            await writer.WriteAsync(SystemUserId, NpgsqlDbType.Uuid); // granted_by
            await writer.WriteAsync("SEED", NpgsqlDbType.Text); // source
            await writer.WriteNullAsync(); // source_ref_id
            await writer.WriteNullAsync(); // revoked_by
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz); // created_at
            await writer.WriteAsync(SystemUserId, NpgsqlDbType.Uuid); // created_by
            await writer.WriteAsync(now, NpgsqlDbType.TimestampTz); // updated_at
            await writer.WriteAsync(SystemUserId, NpgsqlDbType.Uuid); // updated_by
        }

        await writer.CompleteAsync();
    }

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

        foreach (var (user, businessUnit) in new[] { (Admin, (Guid?)null), (UserA, BusinessUnitA.Id), (UserB, BusinessUnitB.Id), (WhA, BusinessUnitA.Id) })
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

        // Step 0.6: grant the matching access.role_assignment to each user, mirroring
        // db/seed/dev/V202610300904__seed__access_dev.sql. Role ids are resolved by
        // code against the roles db/seed/common seeds already applied above
        // (ApplyCommonSeedAsync).
        const string AssignmentSql =
            "INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.role WHERE code = $3), $4, $5, now(), NULL, " +
            "'00000000-0000-7000-8000-000000000001', 'SEED', NULL, NULL, " + Std + ")";

        foreach (var (user, roleCode, scopeType, scopeEntityId) in new (OrgTestUser User, string RoleCode, string ScopeType, Guid? ScopeEntityId)[]
                 {
                     (Admin, "SYSTEM_ADMIN", "GLOBAL", null),
                     (UserA, "ORG_ADMIN", "LEGAL_ENTITY", LegalEntityA.Id),
                     (UserB, "ORG_ADMIN", "LEGAL_ENTITY", LegalEntityB.Id),
                     (WhA, "WAREHOUSE_OPERATOR", "WAREHOUSE", WarehouseA),
                 })
        {
            await Exec(AssignmentSql, NewId(), user.Id, roleCode, scopeType, scopeEntityId);
        }

        await transaction.CommitAsync();
    }
}

/// <summary>A group-internal organisational unit seeded by <see cref="OrgFixture"/> (legal entity or business unit).</summary>
public sealed record OrgUnit(Guid Id, Guid PartyId, string Code);

/// <summary>A user seeded by <see cref="OrgFixture"/>: <c>app_user</c> + its <c>PERSON</c> party; language <c>de</c>.</summary>
public sealed record OrgTestUser(Guid Id, Guid PartyId, string Oid, string Email, string FullName);

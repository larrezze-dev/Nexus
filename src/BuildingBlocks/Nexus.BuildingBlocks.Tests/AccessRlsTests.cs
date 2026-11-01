using System.Diagnostics;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// The RLS policy template (conventions-database.md §7) and the <c>access.visible_*()</c> scope
/// functions (domain/access.md §4, ADR-0004), exercised on <c>test.rls_probe</c>
/// (db/test-migrations) against the two Mandanten of <see cref="OrgFixture"/>.
///
/// <para>
/// <b>How rows are arranged, and why.</b> <c>test.rls_probe</c> has <c>FORCE ROW LEVEL
/// SECURITY</c>, and <c>nexus_owner</c> is not <c>BYPASSRLS</c>, so the owner connection behind
/// <see cref="OrgFixture.AsUser{T}"/>/<see cref="OrgFixture.AsNoUser{T}"/> is subject to the
/// policy like any application session: with no <c>app.current_user_id</c> every
/// <c>visible_*()</c> array is empty and the <c>WITH CHECK</c> rejects every insert (proven by
/// <see cref="Insert_WithoutSession_IsRejectedByWithCheck"/>). Both helpers also always
/// <c>ROLLBACK</c>, so they cannot leave arranged data behind for a later read anyway. Arranged
/// rows (probe rows, test-local users and their <c>role_assignment</c>s) are therefore committed
/// through the superuser data source (<see cref="PostgresFixture.Admin"/>): a superuser always
/// bypasses RLS, <c>FORCE</c> or not, which is exactly what arrangement needs and never what is
/// under test. Every read under test goes through <c>AsUser</c>/<c>AsNoUser</c>.
/// </para>
///
/// <para>
/// Each test seeds its own fresh rows/users (UUIDv7) and asserts only on those ids, or on
/// properties every visible row must satisfy, never on table-wide counts.
/// </para>
/// </summary>
public sealed class AccessRlsTests(OrgFixture org) : IClassFixture<OrgFixture>
{
    private const string InsufficientPrivilege = "42501";
    private const string Std = "now(), '00000000-0000-7000-8000-000000000001', now(), '00000000-0000-7000-8000-000000000001'";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ------------------------------------------------------------------------------------------
    // 1. Visibility by scope
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "RLS: UserA (LEGAL_ENTITY = LE-A) sees only LE-A rows, never LE-B rows")]
    public async Task UserA_SeesOnlyLegalEntityA()
    {
        var seed = await SeedProbeRowsAsync();

        var rows = await org.AsUser(org.UserA.Id, ReadProbeAsync);

        rows.Select(r => r.Id).ShouldContain(seed.LeA);
        rows.Select(r => r.Id).ShouldContain(seed.LeAWhA);
        RlsAssertions.AssertInvisible(rows, r => r.Id, seed.LeB, seed.LeBWhB);
        rows.ShouldAllBe(r => r.LegalEntityId == org.LegalEntityA.Id);
    }

    [Fact(DisplayName = "RLS: UserB (LEGAL_ENTITY = LE-B) sees only LE-B rows, never LE-A rows")]
    public async Task UserB_SeesOnlyLegalEntityB()
    {
        var seed = await SeedProbeRowsAsync();

        var rows = await org.AsUser(org.UserB.Id, ReadProbeAsync);

        rows.Select(r => r.Id).ShouldContain(seed.LeB);
        rows.Select(r => r.Id).ShouldContain(seed.LeBWhB);
        RlsAssertions.AssertInvisible(rows, r => r.Id, seed.LeA, seed.LeAWhA);
        rows.ShouldAllBe(r => r.LegalEntityId == org.LegalEntityB.Id);
    }

    [Fact(DisplayName = "RLS: WhA (WAREHOUSE = WH-A) sees only WH-A rows, not every LE-A row (WAREHOUSE is narrower than LE)")]
    public async Task WhA_SeesOnlyWarehouseARows()
    {
        var seed = await SeedProbeRowsAsync();

        var rows = await org.AsUser(org.WhA.Id, ReadProbeAsync);

        rows.Select(r => r.Id).ShouldContain(seed.LeAWhA);
        // seed.LeA is in WH-A's own legal entity, yet invisible: the WAREHOUSE scope does not
        // widen to the legal entity that owns the warehouse.
        RlsAssertions.AssertInvisible(rows, r => r.Id, seed.LeA, seed.LeB, seed.LeBWhB);
        rows.ShouldAllBe(r => r.WarehouseId == org.WarehouseA);
    }

    [Fact(DisplayName = "RLS: Admin (GLOBAL) sees rows of every legal entity")]
    public async Task Admin_SeesEverything()
    {
        var seed = await SeedProbeRowsAsync();

        var rows = await org.AsUser(org.Admin.Id, ReadProbeAsync);

        AssertVisible(rows, seed.LeA, seed.LeB, seed.LeAWhA, seed.LeBWhB);
    }

    // ------------------------------------------------------------------------------------------
    // 2. WITH CHECK on insert
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "RLS WITH CHECK: UserA cannot INSERT a row for LE-B (42501, new row violates row-level security policy)")]
    public async Task UserA_InsertForLegalEntityB_IsRejectedByWithCheck()
    {
        var id = Guid.CreateVersion7();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            org.AsUser(org.UserA.Id, c => InsertProbeAsync(c, id, org.LegalEntityB.Id, null)));

        ex.SqlState.ShouldBe(InsufficientPrivilege);
        ex.MessageText.ShouldBe("new row violates row-level security policy for table \"rls_probe\"");
        (await org.Database.ScalarAsync<long>("SELECT count(*) FROM test.rls_probe WHERE id = $1", id)).ShouldBe(0);
    }

    [Fact(DisplayName = "RLS WITH CHECK (positive control): UserA can INSERT a row for its own LE-A")]
    public async Task UserA_InsertForLegalEntityA_IsAccepted()
    {
        var id = Guid.CreateVersion7();

        var visibleInsideTransaction = await org.AsUser(org.UserA.Id, async c =>
        {
            await InsertProbeAsync(c, id, org.LegalEntityA.Id, null);
            return (await ReadProbeAsync(c)).Any(r => r.Id == id);
        });

        visibleInsideTransaction.ShouldBeTrue();
    }

    [Fact(DisplayName = "RLS WITH CHECK: the owner connection with no app.current_user_id cannot INSERT (FORCE applies to nexus_owner)")]
    public async Task Insert_WithoutSession_IsRejectedByWithCheck()
    {
        var id = Guid.CreateVersion7();

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            org.AsNoUser(c => InsertProbeAsync(c, id, org.LegalEntityA.Id, null)));

        ex.SqlState.ShouldBe(InsufficientPrivilege);
        ex.MessageText.ShouldBe("new row violates row-level security policy for table \"rls_probe\"");
    }

    // ------------------------------------------------------------------------------------------
    // 3. No session, no rows
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "RLS: without app.current_user_id the policy returns zero rows, whatever is seeded")]
    public async Task NoSession_SeesNoRows()
    {
        await SeedProbeRowsAsync();
        // Positive control: the rows really are there (superuser bypasses RLS).
        (await org.Database.ScalarAsync<long>("SELECT count(*) FROM test.rls_probe")).ShouldBeGreaterThan(0);

        var rows = await org.AsNoUser(ReadProbeAsync);

        rows.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------------------
    // 4./5. Cascades
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "RLS cascade: a GROUP-scoped user sees rows of both LE-A and LE-B (both belong to the group)")]
    public async Task GroupScope_SeesBothLegalEntities()
    {
        var seed = await SeedProbeRowsAsync();
        var userId = await CreateUserAsync();
        await GrantAsync(userId, "ORG_ADMIN", "GROUP", org.GroupId);

        var rows = await org.AsUser(userId, ReadProbeAsync);

        AssertVisible(rows, seed.LeA, seed.LeB, seed.LeAWhA, seed.LeBWhB);
    }

    [Fact(DisplayName = "RLS cascade: a BUSINESS_UNIT-scoped user (BU-A) sees WH-A rows via visible_warehouses(), not other LE-A rows")]
    public async Task BusinessUnitScope_SeesWarehousesUnderIt()
    {
        var seed = await SeedProbeRowsAsync();
        var userId = await CreateUserAsync();
        await GrantAsync(userId, "ORG_ADMIN", "BUSINESS_UNIT", org.BusinessUnitA.Id);

        var (warehouses, rows) = await org.AsUser(userId, async c =>
            (await ScalarArrayAsync(c, "SELECT access.visible_warehouses()"), await ReadProbeAsync(c)));

        warehouses.ShouldContain(org.WarehouseA);
        warehouses.ShouldNotContain(org.WarehouseB);
        // seed.LeAWhA has business_unit_id NULL and an LE (A) this user does not see as a legal
        // entity: the only policy branch that can match it is warehouse_id = ANY(visible_warehouses()).
        rows.Select(r => r.Id).ShouldContain(seed.LeAWhA);
        RlsAssertions.AssertInvisible(rows, r => r.Id, seed.LeA, seed.LeB, seed.LeBWhB);
    }

    // ------------------------------------------------------------------------------------------
    // 6./7. ADR-0004 (a) revocation, (b) expiry
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Measured behavior (pinned here on purpose): a revocation committed as
    /// <c>valid_to = now()</c> while another transaction is open does <b>not</b> take effect
    /// inside that open transaction, even on its next statement. The new row version is
    /// visible to it (READ COMMITTED), but <c>active_assignments()</c> compares against
    /// <c>now()</c>, which is the <i>start</i> time of the reading transaction -- earlier than
    /// the revoking transaction's <c>now()</c> -- so <c>valid_to &gt; now()</c> still holds.
    /// It takes effect on the next transaction, which is what ADR-0004's "Cómo se verifica" (a)
    /// requires.
    /// </summary>
    [Fact(DisplayName = "ADR-0004 (a): a revoked assignment stops granting visibility from the next transaction (not inside one already open)")]
    public async Task Revocation_AppliesOnNextTransaction()
    {
        var seed = await SeedProbeRowsAsync();
        var userId = await CreateUserAsync();
        var assignmentId = await GrantAsync(userId, "ORG_ADMIN", "LEGAL_ENTITY", org.LegalEntityA.Id);

        (await org.AsUser(userId, ReadProbeAsync)).Select(r => r.Id).ShouldContain(seed.LeA);

        var (beforeRevoke, afterRevokeSameTransaction) = await org.AsUser(userId, async c =>
        {
            var before = (await ReadProbeAsync(c)).Any(r => r.Id == seed.LeA);

            // Revoke from another session and commit, while this transaction stays open.
            await ExecAdminAsync(
                "UPDATE access.role_assignment SET valid_to = now(), revoked_by = $2, updated_at = now(), updated_by = $2 WHERE id = $1",
                assignmentId,
                OrgFixture.SystemUserId);

            // Same open transaction: now() is still this transaction's start time (see summary).
            var after = (await ReadProbeAsync(c)).Any(r => r.Id == seed.LeA);
            return (before, after);
        });

        beforeRevoke.ShouldBeTrue();
        afterRevokeSameTransaction.ShouldBeTrue("valid_to = now() of a later transaction is still > now() of this one");

        var nextTransaction = await org.AsUser(userId, ReadProbeAsync);
        RlsAssertions.AssertInvisible(nextTransaction, r => r.Id, seed.LeA, seed.LeAWhA, seed.LeB, seed.LeBWhB);
    }

    [Fact(DisplayName = "ADR-0004 (b): an assignment with valid_to = now() + 2 s stops granting visibility once it expires, without any event")]
    public async Task Expiry_AppliesWithoutEvent()
    {
        var seed = await SeedProbeRowsAsync();
        var userId = await CreateUserAsync();
        await GrantAsync(userId, "ORG_ADMIN", "LEGAL_ENTITY", org.LegalEntityA.Id, validToSql: "now() + interval '2 seconds'");

        (await org.AsUser(userId, ReadProbeAsync)).Select(r => r.Id).ShouldContain(seed.LeA);

        await Task.Delay(TimeSpan.FromSeconds(3), Ct);

        var afterExpiry = await org.AsUser(userId, ReadProbeAsync);
        RlsAssertions.AssertInvisible(afterExpiry, r => r.Id, seed.LeA, seed.LeAWhA);
    }

    // ------------------------------------------------------------------------------------------
    // 8. ADR-0004 (c): benchmark (informational, no threshold asserted)
    // ------------------------------------------------------------------------------------------

    [Fact(DisplayName = "ADR-0004 (c): visible_legal_entities() latency with 10 000 role assignments (informational p95, no threshold)")]
    public async Task Benchmark_VisibleLegalEntities_10kAssignments()
    {
        const int Count = 10_000;
        var roleId = (await org.Database.ScalarAsync<Guid>("SELECT id FROM access.role WHERE code = 'ORG_ADMIN'"))!;
        Guid[] les = [org.LegalEntityA.Id, org.LegalEntityB.Id];

        // Realistic shape first (10 000 assignments, one per distinct user), so it is measured
        // on a table holding only those 10 000 (+ the fixture's own); the pathological shape
        // is then measured with both sets present (~20 000 rows).
        var users = await CreateManyUsersAsync(Count);
        await org.SeedManyRoleAssignmentsAsync(i => users[i], roleId, "LEGAL_ENTITY", i => les[i % 2], Count);
        await AnalyzeAsync();
        var realistic = await MeasureAsync(users[Count / 2]);
        realistic.Result.ShouldBe([les[(Count / 2) % 2]]);

        var heavyUser = await CreateUserAsync();
        await org.SeedManyRoleAssignmentsAsync(_ => heavyUser, roleId, "LEGAL_ENTITY", i => les[i % 2], Count);
        await AnalyzeAsync();
        var pathological = await MeasureAsync(heavyUser);
        pathological.Result.Order().ShouldBe(les.Order());

        var output = TestContext.Current.TestOutputHelper;
        foreach (var (shape, m) in new[] { ("realistic (10k users x 1)", realistic), ("pathological (1 user x 10k)", pathological) })
        {
            var line = FormattableString.Invariant(
                $"ADR-0004 benchmark [{shape}]: n={m.Samples}, client round-trip p50={m.ClientP50:F3} ms p95={m.ClientP95:F3} ms; server execution p50={m.ServerP50:F3} ms p95={m.ServerP95:F3} ms");
            output?.WriteLine(line);
            Console.WriteLine(line);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------

    private sealed record ProbeRow(Guid Id, Guid LegalEntityId, Guid? BusinessUnitId, Guid? WarehouseId);

    /// <summary>Ids of the four probe rows a test seeds: LE only, and LE + its own warehouse, per Mandant.</summary>
    private sealed record ProbeSeed(Guid LeA, Guid LeB, Guid LeAWhA, Guid LeBWhB);

    private sealed record Measurement(int Samples, double ClientP50, double ClientP95, double ServerP50, double ServerP95, Guid[] Result);

    private async Task<ProbeSeed> SeedProbeRowsAsync()
    {
        var seed = new ProbeSeed(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (var (id, le, wh) in new (Guid, Guid, Guid?)[]
                 {
                     (seed.LeA, org.LegalEntityA.Id, null),
                     (seed.LeB, org.LegalEntityB.Id, null),
                     (seed.LeAWhA, org.LegalEntityA.Id, org.WarehouseA),
                     (seed.LeBWhB, org.LegalEntityB.Id, org.WarehouseB),
                 })
        {
            await ExecAdminAsync(
                "INSERT INTO test.rls_probe (id, legal_entity_id, business_unit_id, warehouse_id, project_id, payload) VALUES ($1, $2, NULL, $3, NULL, 'seed')",
                id,
                le,
                wh);
        }

        return seed;
    }

    private static async Task<List<ProbeRow>> ReadProbeAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id, legal_entity_id, business_unit_id, warehouse_id FROM test.rls_probe", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<ProbeRow>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new ProbeRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        }

        return rows;
    }

    private static async Task InsertProbeAsync(NpgsqlConnection connection, Guid id, Guid legalEntityId, Guid? warehouseId)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO test.rls_probe (id, legal_entity_id, warehouse_id, payload) VALUES ($1, $2, $3, 'test')", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = id });
        command.Parameters.Add(new NpgsqlParameter { Value = legalEntityId });
        command.Parameters.Add(new NpgsqlParameter<Guid?> { TypedValue = warehouseId, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid });
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<Guid[]> ScalarArrayAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid[])(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>A brand-new <c>PERSON</c> party + active <c>app_user</c>, committed (same shape as <see cref="OrgFixture"/>'s users).</summary>
    private async Task<Guid> CreateUserAsync()
    {
        var userId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        await ExecAdminAsync(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, 'PERSON', 'RLS test user', true, true, " + Std + ")",
            partyId);
        await ExecAdminAsync(
            "INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, NULL, $3, $4, 'de', true, " + Std + ")",
            userId,
            partyId,
            userId.ToString("D"),
            $"rls-{userId:N}@test.nexus.local");
        return userId;
    }

    /// <summary>
    /// <paramref name="count"/> users in two set-based statements (ids generated here, UUIDv7,
    /// ADR-0009), for the ADR-0004 benchmark's realistic shape.
    /// </summary>
    private async Task<Guid[]> CreateManyUsersAsync(int count)
    {
        var userIds = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        var partyIds = Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToArray();
        await ExecAdminAsync(
            "INSERT INTO org.party (id, party_type, legal_name, is_internal, is_active, created_at, created_by, updated_at, updated_by) " +
            "SELECT p, 'PERSON', 'RLS bench user', true, true, " + Std + " FROM unnest($1::uuid[]) AS p",
            partyIds);
        await ExecAdminAsync(
            "INSERT INTO org.app_user (id, party_id, business_unit_id, external_subject, email, preferred_language, is_active, created_at, created_by, updated_at, updated_by) " +
            "SELECT u, p, NULL, u::text, 'bench-' || u::text || '@test.nexus.local', 'de', true, " + Std + " " +
            "FROM unnest($1::uuid[], $2::uuid[]) AS t(u, p)",
            userIds,
            partyIds);
        return userIds;
    }

    private async Task<Guid> GrantAsync(Guid userId, string roleCode, string scopeType, Guid? scopeEntityId, string validToSql = "NULL")
    {
        var id = Guid.CreateVersion7();
        await ExecAdminAsync(
            "INSERT INTO access.role_assignment (id, user_id, role_id, scope_type, scope_entity_id, valid_from, valid_to, granted_by, source, source_ref_id, revoked_by, created_at, created_by, updated_at, updated_by) " +
            "VALUES ($1, $2, (SELECT id FROM access.role WHERE code = $3), $4, $5, now(), " + validToSql + ", " +
            "'00000000-0000-7000-8000-000000000001', 'MANUAL', NULL, NULL, " + Std + ")",
            id,
            userId,
            roleCode,
            scopeType,
            scopeEntityId);
        return id;
    }

    private Task AnalyzeAsync() => ExecAdminAsync("ANALYZE access.role_assignment; ANALYZE org.app_user");

    /// <summary>Superuser arrangement, committed (autocommit). Never used for a read under test.</summary>
    private async Task ExecAdminAsync(string sql, params object?[] values)
    {
        await using var command = org.Database.Admin.CreateCommand(sql);
        foreach (var value in values)
        {
            command.Parameters.Add(value is null
                ? new NpgsqlParameter { Value = DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Uuid }
                : new NpgsqlParameter { Value = value });
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    /// <summary>
    /// p50/p95 of <c>SELECT access.visible_legal_entities()</c> as <paramref name="userId"/>, on
    /// the owner connection inside one <see cref="OrgFixture.AsUser{T}"/> transaction: client
    /// round-trip (Stopwatch) and server-side "Execution Time" of <c>EXPLAIN ANALYZE</c>.
    /// The first <c>Warmup</c> calls of each kind are discarded.
    /// </summary>
    private Task<Measurement> MeasureAsync(Guid userId) =>
        org.AsUser(userId, async c =>
        {
            const int Warmup = 5;
            const int Samples = 100;

            var client = new List<double>(Samples);
            Guid[] result = [];
            await using (var select = new NpgsqlCommand("SELECT access.visible_legal_entities()", c))
            {
                for (var i = 0; i < Warmup + Samples; i++)
                {
                    var sw = Stopwatch.StartNew();
                    result = (Guid[])(await select.ExecuteScalarAsync(Ct))!;
                    sw.Stop();
                    if (i >= Warmup)
                    {
                        client.Add(sw.Elapsed.TotalMilliseconds);
                    }
                }
            }

            var server = new List<double>(Samples);
            await using (var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, FORMAT JSON) SELECT access.visible_legal_entities()", c))
            {
                for (var i = 0; i < Warmup + Samples; i++)
                {
                    var json = (string)(await explain.ExecuteScalarAsync(Ct))!;
                    using var doc = JsonDocument.Parse(json);
                    var ms = doc.RootElement[0].GetProperty("Execution Time").GetDouble();
                    if (i >= Warmup)
                    {
                        server.Add(ms);
                    }
                }
            }

            return new Measurement(Samples, Percentile(client, 0.50), Percentile(client, 0.95), Percentile(server, 0.50), Percentile(server, 0.95), result);
        });

    /// <summary>Every one of <paramref name="expectedIds"/> is among the visible <paramref name="rows"/>.</summary>
    private static void AssertVisible(IEnumerable<ProbeRow> rows, params Guid[] expectedIds)
    {
        var visible = rows.Select(r => r.Id).ToHashSet();
        expectedIds.Where(id => !visible.Contains(id)).ShouldBeEmpty("expected ids missing from the visible rows");
    }

    /// <summary>Nearest-rank percentile.</summary>
    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(p * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

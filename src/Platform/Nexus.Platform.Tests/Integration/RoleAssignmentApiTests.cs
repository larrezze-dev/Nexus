using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Testing;
using Shouldly;

namespace Nexus.Platform.Tests.Integration;

/// <summary>
/// <c>/v1/role-assignments</c> and <c>/v1/users/{id}/effective-scope</c> end to end: real host,
/// real JWT, real PostgreSQL (all Flyway migrations + <see cref="OrgFixture"/>), connecting as
/// <c>svc_platform</c>.
/// </summary>
public sealed class RoleAssignmentApiTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri RoleAssignments = new("/v1/role-assignments", UriKind.Relative);

    /// <summary>The seeded <c>VIEWER</c> role (db/seed/common/V202610300900__seed__access_roles.sql): harmless to grant/revoke in tests.</summary>
    private static readonly Guid ViewerRoleId = new("00000000-0000-7000-8000-0000000d000b");

    private readonly PlatformApiFixture _fixture;

    public RoleAssignmentApiTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private OrgFixture Org => _fixture.Org;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(DisplayName = "createRoleAssignment + revokeRoleAssignment: a full grant/revoke round trip")]
    public async Task Create_and_revoke_round_trip_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var createRequest = new CreateRoleAssignment(
            Id: Guid.CreateVersion7(),
            UserId: Org.UserB.Id,
            RoleId: ViewerRoleId,
            ScopeType: "LEGAL_ENTITY",
            ScopeEntityId: Org.LegalEntityB.Id,
            ValidFrom: RoundedSystemTimeProvider.Instance.GetUtcNow(),
            ValidTo: null);

        var createResponse = await admin.PostAsJsonAsync(RoleAssignments, createRequest, Ct);
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await ReadAsync(createResponse);
        created.GetProperty("userId").GetGuid().ShouldBe(Org.UserB.Id);
        created.GetProperty("roleId").GetGuid().ShouldBe(ViewerRoleId);
        created.GetProperty("scopeType").GetString().ShouldBe("LEGAL_ENTITY");
        created.GetProperty("scopeEntityId").GetGuid().ShouldBe(Org.LegalEntityB.Id);
        created.GetProperty("source").GetString().ShouldBe("MANUAL");
        created.GetProperty("revokedBy").ValueKind.ShouldBe(JsonValueKind.Null);

        var etag = createResponse.Headers.ETag!.Tag;
        var id = created.GetProperty("id").GetGuid();

        var revokeResponse = await RevokeAsync(admin, id, etag);
        revokeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var revoked = await ReadAsync(revokeResponse);
        revoked.GetProperty("validTo").ValueKind.ShouldNotBe(JsonValueKind.Null);
        revoked.GetProperty("revokedBy").GetGuid().ShouldBe(Org.Admin.Id);
    }

    [Fact(DisplayName = "ACC-R1: GLOBAL scope must not carry a scopeEntityId")]
    public async Task Create_global_with_scope_entity_id_is_rejected()
    {
        // The other half of ACC-R1 ("non-GLOBAL without scopeEntityId") never reaches this
        // handler to be asserted here: AccessPermissionChecker's x-scope resolution
        // (RoleAssignmentScopeResolver) already fails closed with 403 for that shape before any
        // command is dispatched (there is no scope to resolve a target from) - covered instead
        // by the pure domain test (RoleAssignmentTests, no HTTP layer involved).
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var request = new CreateRoleAssignment(
            Id: Guid.CreateVersion7(),
            UserId: Org.UserB.Id,
            RoleId: ViewerRoleId,
            ScopeType: "GLOBAL",
            ScopeEntityId: Guid.CreateVersion7(),
            ValidFrom: RoundedSystemTimeProvider.Instance.GetUtcNow(),
            ValidTo: null);

        var response = await admin.PostAsJsonAsync(RoleAssignments, request, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R1");
    }

    [Fact(DisplayName = "ACC-R2: scopeType PROJECT is rejected before step 2.1")]
    public async Task Create_with_PROJECT_scope_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var request = new CreateRoleAssignment(
            Id: Guid.CreateVersion7(),
            UserId: Org.UserB.Id,
            RoleId: ViewerRoleId,
            ScopeType: "PROJECT",
            ScopeEntityId: Guid.CreateVersion7(),
            ValidFrom: RoundedSystemTimeProvider.Instance.GetUtcNow(),
            ValidTo: null);

        var response = await admin.PostAsJsonAsync(RoleAssignments, request, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R2");
    }

    [Fact(DisplayName = "An unknown scopeType filter is a 400 VALIDATION on list (contract enum)")]
    public async Task List_with_unknown_scope_type_is_a_validation_error()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri("/v1/role-assignments?scopeType=COUNTRY", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("VALIDATION");
    }

    [Fact(DisplayName = "ACC-R5: a user cannot grant a role assignment to themselves")]
    public async Task Create_self_grant_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var request = new CreateRoleAssignment(
            Id: Guid.CreateVersion7(),
            UserId: Org.Admin.Id,
            RoleId: ViewerRoleId,
            ScopeType: "GLOBAL",
            ScopeEntityId: null,
            ValidFrom: RoundedSystemTimeProvider.Instance.GetUtcNow(),
            ValidTo: null);

        var response = await admin.PostAsJsonAsync(RoleAssignments, request, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R5");
    }

    [Fact(DisplayName = "ACC-R5: a user cannot revoke their own role assignment")]
    public async Task Revoke_own_assignment_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (id, etag) = await GetSeededAdminGlobalAssignmentAsync();

        var response = await RevokeAsync(admin, id, etag);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R5");
    }

    [Fact(DisplayName = "ACC-R6: the seeded GLOBAL role assignment cannot be revoked through the API")]
    public async Task Revoke_seeded_global_assignment_by_a_different_user_is_rejected()
    {
        // ACC-R6 needs a caller whose own scope covers GLOBAL (anyone else is a 403 first): a
        // second, MANUAL GLOBAL administrator, granted for this test and revoked afterwards.
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var systemAdminRoleId = await _fixture.Database.ScalarAsync<Guid>("SELECT id FROM access.role WHERE code = 'SYSTEM_ADMIN'");
        var (secondAdminGrantId, secondAdminGrantETag) = await CreateAsync(
            admin, Org.UserB.Id, systemAdminRoleId, "GLOBAL", null, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);

        try
        {
            using var secondAdmin = _fixture.CreateClientFor(Org.UserB.Oid);
            var (id, etag) = await GetSeededAdminGlobalAssignmentAsync();

            var response = await RevokeAsync(secondAdmin, id, etag);

            response.StatusCode.ShouldBe((HttpStatusCode)422);
            (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R6");
        }
        finally
        {
            (await RevokeAsync(admin, secondAdminGrantId, secondAdminGrantETag)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact(DisplayName = "ACC-R4/ACC-R10 on revoke: an ORG_ADMIN of LE-A cannot revoke an assignment scoped to LE-B")]
    public async Task Revoke_assignment_outside_the_callers_scope_is_forbidden()
    {
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var (id, etag) = await GetSeededAssignmentAsync(Org.UserB.Id, "LEGAL_ENTITY");

        var response = await RevokeAsync(userA, id, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("FORBIDDEN");
        (await _fixture.Database.ScalarAsync<bool>(
            "SELECT valid_to IS NULL AND revoked_by IS NULL FROM access.role_assignment WHERE id = $1", id)).ShouldBeTrue();
    }

    [Fact(DisplayName = "ACC-R4/ACC-R10 on revoke: an ORG_ADMIN of LE-A cannot revoke a second GLOBAL administrator")]
    public async Task Revoke_global_assignment_by_a_non_global_caller_is_forbidden()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var systemAdminRoleId = await _fixture.Database.ScalarAsync<Guid>("SELECT id FROM access.role WHERE code = 'SYSTEM_ADMIN'");
        var (grantId, grantETag) = await CreateAsync(admin, Org.WhA.Id, systemAdminRoleId, "GLOBAL", null, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);

        try
        {
            using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
            var response = await RevokeAsync(userA, grantId, grantETag);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            (await RevokeAsync(admin, grantId, grantETag)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Theory(DisplayName = "ACC-R4/ACC-R10 on revoke: an ORG_ADMIN of LE-A can revoke assignments within LE-A")]
    [InlineData("LEGAL_ENTITY")]
    [InlineData("BUSINESS_UNIT")]
    [InlineData("WAREHOUSE")]
    public async Task Revoke_assignment_inside_the_callers_scope_succeeds(string scopeType)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var scopeEntityId = scopeType switch
        {
            "LEGAL_ENTITY" => Org.LegalEntityA.Id,
            "BUSINESS_UNIT" => Org.BusinessUnitA.Id,
            _ => Org.WarehouseA,
        };
        var (id, etag) = await CreateAsync(admin, Org.UserB.Id, ViewerRoleId, scopeType, scopeEntityId, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var response = await RevokeAsync(userA, id, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(response)).GetProperty("revokedBy").GetGuid().ShouldBe(Org.UserA.Id);
    }

    [Fact(DisplayName = "A GLOBAL administrator can revoke an assignment in any legal entity")]
    public async Task Revoke_by_global_admin_covers_every_scope()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (id, etag) = await CreateAsync(admin, Org.UserA.Id, ViewerRoleId, "WAREHOUSE", Org.WarehouseB, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);

        var response = await RevokeAsync(admin, id, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "ACC-R3: revoking an active assignment with a future validTo sets validTo to now, not a no-op")]
    public async Task Revoke_active_assignment_with_future_valid_to_revokes_it()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var futureValidTo = RoundedSystemTimeProvider.Instance.GetUtcNow().AddYears(1);
        var (id, etag) = await CreateAsync(
            admin, Org.UserB.Id, ViewerRoleId, "LEGAL_ENTITY", Org.LegalEntityB.Id, RoundedSystemTimeProvider.Instance.GetUtcNow(), futureValidTo);

        var before = RoundedSystemTimeProvider.Instance.GetUtcNow();
        var response = await RevokeAsync(admin, id, etag);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        var validTo = body.GetProperty("validTo").GetDateTimeOffset();
        validTo.ShouldBeInRange(before.AddMinutes(-1), RoundedSystemTimeProvider.Instance.GetUtcNow().AddMinutes(1));
        body.GetProperty("revokedBy").GetGuid().ShouldBe(Org.Admin.Id);

        var persisted = await _fixture.Database.ScalarAsync<DateTime>(
            "SELECT valid_to FROM access.role_assignment WHERE id = $1 AND revoked_by IS NOT NULL", id);
        new DateTimeOffset(persisted, TimeSpan.Zero).ShouldBeLessThan(futureValidTo.AddDays(-300));
    }

    [Fact(DisplayName = "ACC-R2: a WAREHOUSE scope with an existing warehouse is created")]
    public async Task Create_with_existing_warehouse_scope_succeeds()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(
            RoleAssignments,
            new CreateRoleAssignment(Guid.CreateVersion7(), Org.UserB.Id, ViewerRoleId, "WAREHOUSE", Org.WarehouseB, RoundedSystemTimeProvider.Instance.GetUtcNow(), null),
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory(DisplayName = "ACC-R2: scopeEntityId must exist in the table scopeType names")]
    [InlineData("WAREHOUSE", false)]
    [InlineData("WAREHOUSE", true)]
    [InlineData("LEGAL_ENTITY", false)]
    [InlineData("BUSINESS_UNIT", false)]
    [InlineData("BUSINESS_UNIT", true)]
    [InlineData("GROUP", false)]
    [InlineData("GROUP", true)]
    public async Task Create_with_nonexistent_scope_entity_is_rejected(string scopeType, bool useIdOfAnotherTable)
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        // Either a random id, or a real id from the wrong table (a legal entity's id).
        var scopeEntityId = useIdOfAnotherTable ? Org.LegalEntityA.Id : Guid.CreateVersion7();

        var response = await admin.PostAsJsonAsync(
            RoleAssignments,
            new CreateRoleAssignment(Guid.CreateVersion7(), Org.UserB.Id, ViewerRoleId, scopeType, scopeEntityId, RoundedSystemTimeProvider.Instance.GetUtcNow(), null),
            Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R2");
    }

    [Fact(DisplayName = "createRoleAssignment: validFrom and validTo both in the past is a 422, not a 500")]
    public async Task Create_with_valid_to_in_the_past_is_rejected()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.PostAsJsonAsync(
            RoleAssignments,
            new CreateRoleAssignment(
                Guid.CreateVersion7(),
                Org.UserB.Id,
                ViewerRoleId,
                "LEGAL_ENTITY",
                Org.LegalEntityB.Id,
                new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ROLE_ASSIGNMENT_VALID_TO_IN_PAST");
    }

    [Fact(DisplayName = "ADR-0016: a validFrom with 7 fractional digits is a 400 naming validFrom, and nothing reaches the outbox")]
    public async Task Create_with_sub_microsecond_valid_from_is_a_validation_error()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var subjectId = await CreateUserAsync(admin);
        var id = Guid.CreateVersion7();

        // Raw JSON string, so the 7th digit is exactly what the client sends (ISO-8601 allows it).
        var response = await admin.PostAsJsonAsync(
            RoleAssignments,
            new { id, userId = subjectId, roleId = ViewerRoleId, scopeType = "LEGAL_ENTITY", scopeEntityId = Org.LegalEntityA.Id, validFrom = "2026-01-01T00:00:00.1234567Z" },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await ReadAsync(response);
        problem.GetProperty("code").GetString().ShouldBe("VALIDATION");
        problem.GetProperty("detail").GetString()!.ShouldContain("'validFrom' must not be more precise than one microsecond");
        (await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM outbox.message WHERE aggregate_id = $1", id)).ShouldBe(0);
        (await _fixture.Database.ScalarAsync<long>("SELECT count(*) FROM access.role_assignment WHERE id = $1", id)).ShouldBe(0);
    }

    [Fact(DisplayName = "ADR-0016: a validFrom with 6 fractional digits is a 201, and the response, the row and the role-assignment.changed event all carry exactly that instant")]
    public async Task Create_with_microsecond_valid_from_keeps_it_exactly()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var subjectId = await CreateUserAsync(admin);
        var id = Guid.CreateVersion7();
        var validFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_560); // .123456

        var response = await admin.PostAsJsonAsync(
            RoleAssignments,
            new { id, userId = subjectId, roleId = ViewerRoleId, scopeType = "LEGAL_ENTITY", scopeEntityId = Org.LegalEntityA.Id, validFrom = "2026-01-01T00:00:00.123456Z" },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await ReadAsync(response)).GetProperty("validFrom").GetDateTimeOffset().ShouldBe(validFrom);

        var stored = await _fixture.Database.ScalarAsync<DateTime>("SELECT valid_from FROM access.role_assignment WHERE id = $1", id);
        new DateTimeOffset(stored, TimeSpan.Zero).ShouldBe(validFrom);

        // Avro JSON encoding of timestamp-micros: a bare long, microseconds since the Unix epoch.
        var payload = await _fixture.Database.ScalarAsync<string>(
            "SELECT payload::text FROM outbox.message WHERE aggregate_id = $1 AND event_type = 'platform.role-assignment.changed'", id);
        using var document = JsonDocument.Parse(payload!);
        var expectedMicros = (validFrom.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMicrosecond;
        document.RootElement.GetProperty("validFrom").GetInt64().ShouldBe(expectedMicros);
    }

    [Fact(DisplayName = "The valid_to >= created_at CHECK has the name RoleAssignmentErrors maps")]
    public async Task Valid_to_check_constraint_name_is_pinned()
    {
        var definition = await _fixture.Database.ScalarAsync<string>(
            "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = 'access.role_assignment'::regclass AND conname = $1",
            Nexus.Platform.Application.RoleAssignments.RoleAssignmentErrors.ValidToNotBeforeCreatedAtCheck);

        definition.ShouldBe("CHECK (((valid_to IS NULL) OR (valid_to >= created_at)))");
    }

    [Fact(DisplayName = "getUserEffectiveScope: a WAREHOUSE-scoped user only sees that warehouse")]
    public async Task Get_effective_scope_of_a_warehouse_scoped_user()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri($"/v1/users/{Org.WhA.Id}/effective-scope", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        body.GetProperty("legalEntityIds").GetArrayLength().ShouldBe(0);
        body.GetProperty("businessUnitIds").GetArrayLength().ShouldBe(0);
        body.GetProperty("projectIds").GetArrayLength().ShouldBe(0);
        var warehouseIds = body.GetProperty("warehouseIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        warehouseIds.ShouldBe([Org.WarehouseA]);
    }

    [Fact(DisplayName = "ACC-R10 on listRoleAssignments: an ORG_ADMIN of LE-A does not see an assignment scoped to LE-B; a GLOBAL admin does")]
    public async Task List_only_returns_assignments_the_caller_covers()
    {
        var (userBAssignmentId, _) = await GetSeededAssignmentAsync(Org.UserB.Id, "LEGAL_ENTITY");
        var (userAAssignmentId, _) = await GetSeededAssignmentAsync(Org.UserA.Id, "LEGAL_ENTITY");
        var (whAAssignmentId, _) = await GetSeededAssignmentAsync(Org.WhA.Id, "WAREHOUSE");

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var seenByUserA = await ListAsync(userA, "pageSize=200");

        var idsSeenByUserA = seenByUserA.Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        idsSeenByUserA.ShouldNotContain(userBAssignmentId);
        idsSeenByUserA.ShouldContain(userAAssignmentId);
        idsSeenByUserA.ShouldContain(whAAssignmentId); // WH-A lies beneath LE-A: covered.

        // Nothing outside LE-A's subtree leaks, whatever other tests of this class created.
        var leASubtree = new HashSet<Guid> { Org.LegalEntityA.Id, Org.BusinessUnitA.Id, Org.WarehouseA };
        foreach (var item in seenByUserA.Items)
        {
            item.GetProperty("scopeType").GetString().ShouldNotBe("GLOBAL");
            leASubtree.ShouldContain(item.GetProperty("scopeEntityId").GetGuid());
        }

        seenByUserA.Total.ShouldBe(seenByUserA.Items.Count);

        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var idsSeenByAdmin = (await ListAsync(admin, "pageSize=200")).Items.Select(i => i.GetProperty("id").GetGuid()).ToList();
        idsSeenByAdmin.ShouldContain(userBAssignmentId);
        idsSeenByAdmin.ShouldContain(userAAssignmentId);
    }

    [Fact(DisplayName = "ACC-R10 on listRoleAssignments: pagination and total apply to the coverage-filtered rows, not the unfiltered ones")]
    public async Task List_paginates_and_counts_after_coverage_filtering()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var subjectId = await CreateUserAsync(admin);

        // 2 assignments inside LE-A's subtree, 3 inside LE-B's: only the first 2 are UserA's to see.
        var inLeA = new List<Guid>
        {
            (await CreateAsync(admin, subjectId, ViewerRoleId, "LEGAL_ENTITY", Org.LegalEntityA.Id, RoundedSystemTimeProvider.Instance.GetUtcNow(), null)).Id,
            (await CreateAsync(admin, subjectId, ViewerRoleId, "WAREHOUSE", Org.WarehouseA, RoundedSystemTimeProvider.Instance.GetUtcNow(), null)).Id,
        };
        await CreateAsync(admin, subjectId, ViewerRoleId, "LEGAL_ENTITY", Org.LegalEntityB.Id, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);
        await CreateAsync(admin, subjectId, ViewerRoleId, "BUSINESS_UNIT", Org.BusinessUnitB.Id, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);
        await CreateAsync(admin, subjectId, ViewerRoleId, "WAREHOUSE", Org.WarehouseB, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);

        (await ListAsync(admin, $"userId={subjectId}&pageSize=1")).Total.ShouldBe(5);

        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var page1 = await ListAsync(userA, $"userId={subjectId}&pageSize=1&page=1");
        var page2 = await ListAsync(userA, $"userId={subjectId}&pageSize=1&page=2");
        var page3 = await ListAsync(userA, $"userId={subjectId}&pageSize=1&page=3");

        page1.Total.ShouldBe(2);
        page2.Total.ShouldBe(2);
        page3.Total.ShouldBe(2);
        page1.Items.Count.ShouldBe(1);
        page2.Items.Count.ShouldBe(1);
        page3.Items.ShouldBeEmpty();
        new[] { page1.Items[0].GetProperty("id").GetGuid(), page2.Items[0].GetProperty("id").GetGuid() }
            .ShouldBe(inLeA, ignoreOrder: true);
    }

    [Fact(DisplayName = "getRoleAssignment: returns the assignment with an ETag that revokeRoleAssignment accepts")]
    public async Task Get_role_assignment_by_id_returns_it_with_its_etag()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);
        var (id, createdETag) = await CreateAsync(admin, Org.UserB.Id, ViewerRoleId, "LEGAL_ENTITY", Org.LegalEntityB.Id, RoundedSystemTimeProvider.Instance.GetUtcNow(), null);

        var response = await admin.GetAsync(new Uri($"/v1/role-assignments/{id}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var etag = response.Headers.ETag!.Tag;
        etag.ShouldBe(createdETag);
        var body = await ReadAsync(response);
        body.GetProperty("id").GetGuid().ShouldBe(id);
        body.GetProperty("userId").GetGuid().ShouldBe(Org.UserB.Id);
        body.GetProperty("scopeType").GetString().ShouldBe("LEGAL_ENTITY");
        body.GetProperty("scopeEntityId").GetGuid().ShouldBe(Org.LegalEntityB.Id);
        body.GetProperty("grantedBy").GetGuid().ShouldBe(Org.Admin.Id);
        body.GetProperty("source").GetString().ShouldBe("MANUAL");

        // The ETag is the real concurrency token: revoke accepts it, and a fresh GET follows the change.
        (await RevokeAsync(admin, id, etag)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRevoke = await admin.GetAsync(new Uri($"/v1/role-assignments/{id}", UriKind.Relative), Ct);
        afterRevoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        afterRevoke.Headers.ETag!.Tag.ShouldNotBe(etag);
        (await ReadAsync(afterRevoke)).GetProperty("revokedBy").GetGuid().ShouldBe(Org.Admin.Id);
    }

    [Fact(DisplayName = "getRoleAssignment: an unknown id is a 404")]
    public async Task Get_role_assignment_with_unknown_id_is_not_found()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri($"/v1/role-assignments/{Guid.CreateVersion7()}", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact(DisplayName = "getUserEffectiveScope: an unknown user is a 404")]
    public async Task Get_effective_scope_of_an_unknown_user_is_not_found()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri($"/v1/users/{Guid.CreateVersion7()}/effective-scope", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- helpers ---------------------------------------------------------------------------

    private static async Task<(Guid Id, string ETag)> CreateAsync(
        HttpClient client, Guid userId, Guid roleId, string scopeType, Guid? scopeEntityId, DateTimeOffset validFrom, DateTimeOffset? validTo)
    {
        var response = await client.PostAsJsonAsync(
            RoleAssignments,
            new CreateRoleAssignment(Guid.CreateVersion7(), userId, roleId, scopeType, scopeEntityId, validFrom, validTo),
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return ((await ReadAsync(response)).GetProperty("id").GetGuid(), response.Headers.ETag!.Tag);
    }

    /// <summary><c>listRoleAssignments</c> with <paramref name="query"/>: its items and <c>total</c>.</summary>
    private static async Task<(IReadOnlyList<JsonElement> Items, long Total)> ListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(new Uri($"/v1/role-assignments?{query}", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadAsync(response);
        return (body.GetProperty("items").EnumerateArray().ToList(), body.GetProperty("total").GetInt64());
    }

    /// <summary>A fresh user (own oid/email), so a test can count its assignments without interference.</summary>
    private static async Task<Guid> CreateUserAsync(HttpClient admin)
    {
        var tag = Guid.NewGuid().ToString("N");
        var response = await admin.PostAsJsonAsync(
            new Uri("/v1/users", UriKind.Relative),
            new { oid = "oid-" + tag, fullName = "Assignment Subject " + tag, email = $"subject.{tag}@test.nexus.local", preferredLanguage = "en" },
            Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>A role assignment <see cref="OrgFixture"/> seeded for <paramref name="userId"/>, with its current ETag.</summary>
    private async Task<(Guid Id, string ETag)> GetSeededAssignmentAsync(Guid userId, string scopeType)
    {
        var id = await _fixture.Database.ScalarAsync<Guid>(
            "SELECT id FROM access.role_assignment WHERE user_id = $1 AND scope_type = $2 AND source = 'SEED'",
            userId,
            scopeType);
        var version = await _fixture.Database.ScalarAsync<long>(
            "SELECT xmin::text::bigint FROM access.role_assignment WHERE id = $1", id);
        return (id, $"\"{version}\"");
    }

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid id, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/role-assignments/{id}/revoke", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request, Ct);
    }

    /// <summary>
    /// SYSTEM_ADMIN's seeded <c>GLOBAL</c>/<c>SEED</c> role assignment that <see cref="OrgFixture"/>
    /// grants to <see cref="OrgFixture.Admin"/> (mirroring <c>db/seed/dev</c>, per its own code
    /// comment) - the only row in this fixture matching ACC-R6's predicate. Its id is only
    /// reachable from the database (no API filter singles it out), so its current
    /// <c>xmin</c>-derived <c>ETag</c> is read there too.
    /// </summary>
    private async Task<(Guid Id, string ETag)> GetSeededAdminGlobalAssignmentAsync()
    {
        var id = await _fixture.Database.ScalarAsync<Guid>(
            "SELECT id FROM access.role_assignment WHERE user_id = $1 AND scope_type = 'GLOBAL' AND source = 'SEED'",
            Org.Admin.Id);
        var version = await _fixture.Database.ScalarAsync<long>(
            "SELECT xmin::text::bigint FROM access.role_assignment WHERE id = $1", id);
        return (id, $"\"{version}\"");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonDocument>(Ct))!.RootElement;

    private sealed record CreateRoleAssignment(
        Guid? Id,
        Guid UserId,
        Guid RoleId,
        string ScopeType,
        Guid? ScopeEntityId,
        DateTimeOffset ValidFrom,
        DateTimeOffset? ValidTo);
}

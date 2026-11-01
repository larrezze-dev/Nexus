using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
            ValidFrom: DateTimeOffset.UtcNow,
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
            ValidFrom: DateTimeOffset.UtcNow,
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
            ValidFrom: DateTimeOffset.UtcNow,
            ValidTo: null);

        var response = await admin.PostAsJsonAsync(RoleAssignments, request, Ct);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R2");
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
            ValidFrom: DateTimeOffset.UtcNow,
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
        using var userA = _fixture.CreateClientFor(Org.UserA.Oid);
        var (id, etag) = await GetSeededAdminGlobalAssignmentAsync();

        var response = await RevokeAsync(userA, id, etag);

        response.StatusCode.ShouldBe((HttpStatusCode)422);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("ACC-R6");
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

    [Fact(DisplayName = "getUserEffectiveScope: an unknown user is a 404")]
    public async Task Get_effective_scope_of_an_unknown_user_is_not_found()
    {
        using var admin = _fixture.CreateClientFor(Org.Admin.Oid);

        var response = await admin.GetAsync(new Uri($"/v1/users/{Guid.CreateVersion7()}/effective-scope", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- helpers ---------------------------------------------------------------------------

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient client, Guid id, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/v1/role-assignments/{id}/revoke", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request, Ct);
    }

    /// <summary>
    /// SYSTEM_ADMIN's seeded <c>GLOBAL</c>/<c>SEED</c> role assignment that <see cref="OrgFixture"/>
    /// grants to <see cref="OrgFixture.Admin"/> (mirroring <c>db/seed/dev</c>, per its own code
    /// comment) - the only row in this fixture matching ACC-R6's predicate. Read directly from
    /// the database (there is no <c>GET /v1/role-assignments/{id}</c>) since both its id and its
    /// current <c>xmin</c>-derived <c>ETag</c> are needed to call <c>revoke</c> at all.
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

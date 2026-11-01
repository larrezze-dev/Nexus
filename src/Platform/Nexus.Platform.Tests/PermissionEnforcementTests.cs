using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Nexus.Platform.Tests.Integration;
using Shouldly;

namespace Nexus.Platform.Tests;

/// <summary>
/// Step 0.6: <c>EnforcePermissions</c> is on and <c>AccessPermissionChecker</c> runs end to end
/// in the real host (JWT -> identity resolution -> unit of work -> <c>access.has_permission</c>),
/// through the test-only <c>/v1/_diagnostics/permission-check/*</c> endpoints (excluded from the
/// OpenAPI document). <c>ROLE:CREATE</c> is held only by SYSTEM_ADMIN/SECURITY_ADMIN, so of
/// OrgFixture's users only Admin (SYSTEM_ADMIN GLOBAL) has it.
/// </summary>
public sealed class PermissionEnforcementTests : IClassFixture<PlatformApiFixture>
{
    private static readonly Uri NoneScope = new("/v1/_diagnostics/permission-check/none", UriKind.Relative);
    private static readonly Uri GlobalScope = new("/v1/_diagnostics/permission-check/global", UriKind.Relative);
    private static readonly Uri GrantScope = new("/v1/_diagnostics/permission-check/role-assignment-scope", UriKind.Relative);

    private readonly PlatformApiFixture _fixture;

    public PermissionEnforcementTests(PlatformApiFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrgFixture Org => _fixture.Org;

    private async Task<HttpStatusCode> Get(OrgTestUser user, Uri uri)
    {
        using var client = _fixture.CreateClientFor(user.Oid);
        using var response = await client.GetAsync(uri, Ct);
        return response.StatusCode;
    }

    [Fact]
    public async Task GlobalAdmin_PassesBothNoneAndGlobal()
    {
        (await Get(Org.Admin, NoneScope)).ShouldBe(HttpStatusCode.OK);
        (await Get(Org.Admin, GlobalScope)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UsersWithoutThePermissionAnywhere_GetForbiddenOnBoth()
    {
        foreach (var user in new[] { Org.UserA, Org.WhA })
        {
            (await Get(user, NoneScope)).ShouldBe(HttpStatusCode.Forbidden);
            (await Get(user, GlobalScope)).ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Forbidden_IsAProblemDetailsNamingTheMissingPermission()
    {
        using var client = _fixture.CreateClientFor(Org.UserA.Oid);
        using var response = await client.GetAsync(GlobalScope, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("permission").GetString().ShouldBe("ROLE:CREATE");
    }

    [Fact]
    public async Task WithoutAToken_StillReturns401_NotForbidden()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync(NoneScope, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // Existing endpoints are now really enforced: UserA (ORG_ADMIN at LE-A) holds
    // ORG_STRUCTURE:READ, WhA (WAREHOUSE_OPERATOR) holds ORG_STRUCTURE:READ but not USER:READ.
    [Fact]
    public async Task ExistingEndpoints_AreEnforced()
    {
        (await Get(Org.UserA, new Uri("/v1/legal-entities", UriKind.Relative))).ShouldBe(HttpStatusCode.OK);
        (await Get(Org.WhA, new Uri("/v1/legal-entities", UriKind.Relative))).ShouldBe(HttpStatusCode.OK);
        (await Get(Org.WhA, new Uri("/v1/users", UriKind.Relative))).ShouldBe(HttpStatusCode.Forbidden);
    }

    // x-scope: body.scopeEntityId through the real pipeline: the filter re-reads the body
    // (UseNexusPermissionBodyBuffering) and model binding still gets it intact.
    [Fact]
    public async Task BodyScope_AllowsACoveredGrant_AndTheHandlerStillSeesTheBody()
    {
        using var client = _fixture.CreateClientFor(Org.UserA.Oid);
        using var response = await client.PostAsJsonAsync(
            GrantScope,
            new { scopeType = "WAREHOUSE", scopeEntityId = Org.WarehouseA },
            Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        body.RootElement.GetProperty("scopeType").GetString().ShouldBe("WAREHOUSE");
        body.RootElement.GetProperty("scopeEntityId").GetGuid().ShouldBe(Org.WarehouseA);
    }

    [Fact]
    public async Task BodyScope_DeniesAGrantOutsideTheGrantersScope()
    {
        using var client = _fixture.CreateClientFor(Org.UserA.Oid);

        using var otherLe = await client.PostAsJsonAsync(
            GrantScope, new { scopeType = "LEGAL_ENTITY", scopeEntityId = Org.LegalEntityB.Id }, Ct);
        using var wholeGroup = await client.PostAsJsonAsync(
            GrantScope, new { scopeType = "GROUP", scopeEntityId = Org.GroupId }, Ct);

        otherLe.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        wholeGroup.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

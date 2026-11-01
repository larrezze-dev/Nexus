using System.Text;
using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Web.Authorization;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.Authorization;

/// <summary>
/// <see cref="AccessPermissionChecker"/> against a real <c>access.has_permission</c>, a real
/// <c>UnitOfWork</c> as <c>svc_platform</c> and <see cref="OrgFixture"/>'s seeded assignments:
/// Admin = SYSTEM_ADMIN GLOBAL; UserA = ORG_ADMIN LE-A; WhA = WAREHOUSE_OPERATOR WH-A;
/// BU admin = ORG_ADMIN BU-A (<see cref="AccessPermissionDatabase"/>).
/// </summary>
public sealed class AccessPermissionCheckerTests(AccessPermissionDatabase db) : IClassFixture<AccessPermissionDatabase>
{
    private static readonly PermissionMetadata OrgStructureRead = new("ORG_STRUCTURE", PermissionAction.Read);
    private static readonly PermissionMetadata RoleCreate = new("ROLE", PermissionAction.Create);
    private static readonly PermissionMetadata RoleAssignmentCreate = new("ROLE_ASSIGNMENT", PermissionAction.Create);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrgFixture Org => db.Org;

    private async Task<bool> Check(Guid userId, PermissionMetadata permission, string? scope, HttpContext? httpContext = null)
    {
        await using var uow = db.CreateUnitOfWork(userId);
        var checker = new AccessPermissionChecker(uow);
        var result = await checker.HasPermissionAsync(
            httpContext ?? new DefaultHttpContext(),
            permission,
            scope is null ? null : new PermissionScopeMetadata(scope),
            Ct);

        // The checker begins the request's transaction; the handler's BeginAsync then reuses it.
        uow.IsActive.ShouldBeTrue();
        return result;
    }

    private static DefaultHttpContext BodyContext(string json)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        context.Request.Body.Position = context.Request.Body.Length; // as left by model binding
        return context;
    }

    private Task<bool> CheckGrant(Guid granter, string scopeType, Guid? scopeEntityId)
    {
        var entity = scopeEntityId is { } id ? $"\"{id}\"" : "null";
        return Check(
            granter,
            RoleAssignmentCreate,
            AccessPermissionChecker.ScopeRoleAssignmentBody,
            BodyContext($$"""{"userId":"{{Org.UserB.Id}}","roleId":"{{Guid.CreateVersion7()}}","scopeType":"{{scopeType}}","scopeEntityId":{{entity}}}"""));
    }

    // ---- x-scope: none (ACC-R10: the permission in any scope) --------------------------------

    [Fact]
    public async Task None_AllowsAPermissionHeldInAnyScope()
    {
        (await Check(Org.UserA.Id, OrgStructureRead, "none")).ShouldBeTrue();
        (await Check(Org.WhA.Id, OrgStructureRead, "none")).ShouldBeTrue();
        (await Check(Org.Admin.Id, OrgStructureRead, "none")).ShouldBeTrue();
    }

    [Fact]
    public async Task None_DeniesAPermissionNotHeldAnywhere()
    {
        (await Check(Org.UserA.Id, RoleCreate, "none")).ShouldBeFalse();
        (await Check(Org.WhA.Id, RoleCreate, "none")).ShouldBeFalse();
    }

    [Fact]
    public async Task NoScopeMetadata_BehavesLikeNone()
    {
        (await Check(Org.UserA.Id, OrgStructureRead, scope: null)).ShouldBeTrue();
        (await Check(Org.UserA.Id, RoleCreate, scope: null)).ShouldBeFalse();
    }

    [Fact]
    public async Task UnknownUser_IsDenied()
    {
        (await Check(Guid.CreateVersion7(), OrgStructureRead, "none")).ShouldBeFalse();
    }

    // ---- x-scope: global -------------------------------------------------------------------

    [Fact]
    public async Task Global_AllowsAGlobalAssignment()
    {
        (await Check(Org.Admin.Id, RoleCreate, "global")).ShouldBeTrue();
    }

    // UserA holds ORG_STRUCTURE:READ (ORG_ADMIN at LE-A) and passes "none" with it, but "global"
    // needs a GLOBAL-scoped assignment, not "the permission somewhere".
    [Fact]
    public async Task Global_DeniesAPermissionHeldOnlyInANarrowerScope()
    {
        (await Check(Org.UserA.Id, OrgStructureRead, "global")).ShouldBeFalse();
        (await Check(Org.WhA.Id, OrgStructureRead, "global")).ShouldBeFalse();
        (await Check(db.BuAdminUserId, OrgStructureRead, "global")).ShouldBeFalse();
    }

    [Fact]
    public async Task UnsupportedScope_Throws_InsteadOfWideningToAnyScope()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => Check(Org.UserA.Id, OrgStructureRead, "path.legalEntityId"));
    }

    // ---- x-scope: body.scopeEntityId (ACC-R4) ------------------------------------------------

    [Fact]
    public async Task Grant_GlobalAdminCoversEveryTargetScope()
    {
        (await CheckGrant(Org.Admin.Id, "GLOBAL", null)).ShouldBeTrue();
        (await CheckGrant(Org.Admin.Id, "GROUP", Org.GroupId)).ShouldBeTrue();
        (await CheckGrant(Org.Admin.Id, "LEGAL_ENTITY", Org.LegalEntityB.Id)).ShouldBeTrue();
        (await CheckGrant(Org.Admin.Id, "BUSINESS_UNIT", Org.BusinessUnitB.Id)).ShouldBeTrue();
        (await CheckGrant(Org.Admin.Id, "WAREHOUSE", Org.WarehouseB)).ShouldBeTrue();
    }

    [Fact]
    public async Task Grant_LegalEntityAdminCoversItsOwnLegalEntityAndEverythingBelowIt()
    {
        (await CheckGrant(Org.UserA.Id, "LEGAL_ENTITY", Org.LegalEntityA.Id)).ShouldBeTrue();
        (await CheckGrant(Org.UserA.Id, "BUSINESS_UNIT", Org.BusinessUnitA.Id)).ShouldBeTrue();
        (await CheckGrant(Org.UserA.Id, "WAREHOUSE", Org.WarehouseA)).ShouldBeTrue();
    }

    [Fact]
    public async Task Grant_LegalEntityAdminDoesNotCoverAnotherLegalEntity()
    {
        (await CheckGrant(Org.UserA.Id, "LEGAL_ENTITY", Org.LegalEntityB.Id)).ShouldBeFalse();
        (await CheckGrant(Org.UserA.Id, "BUSINESS_UNIT", Org.BusinessUnitB.Id)).ShouldBeFalse();
        (await CheckGrant(Org.UserA.Id, "WAREHOUSE", Org.WarehouseB)).ShouldBeFalse();
    }

    // The privilege-escalation case: LE-A is in the group, but an LE-A admin must not grant a
    // role over the whole group (it covers LE-B too).
    [Fact]
    public async Task Grant_LegalEntityAdminDoesNotCoverItsGroupOrGlobal()
    {
        (await CheckGrant(Org.UserA.Id, "GROUP", Org.GroupId)).ShouldBeFalse();
        (await CheckGrant(Org.UserA.Id, "GLOBAL", null)).ShouldBeFalse();
    }

    [Fact]
    public async Task Grant_BusinessUnitAdminCoversItsWarehousesButNotItsLegalEntity()
    {
        (await CheckGrant(db.BuAdminUserId, "BUSINESS_UNIT", Org.BusinessUnitA.Id)).ShouldBeTrue();
        (await CheckGrant(db.BuAdminUserId, "WAREHOUSE", Org.WarehouseA)).ShouldBeTrue();
        (await CheckGrant(db.BuAdminUserId, "WAREHOUSE", Org.WarehouseB)).ShouldBeFalse();
        (await CheckGrant(db.BuAdminUserId, "LEGAL_ENTITY", Org.LegalEntityA.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Grant_DeniedWithoutRoleAssignmentCreate_EvenInsideTheOwnScope()
    {
        (await CheckGrant(Org.WhA.Id, "WAREHOUSE", Org.WarehouseA)).ShouldBeFalse();
    }

    [Fact]
    public async Task Grant_MalformedBodies_FailClosed()
    {
        (await CheckGrant(Org.Admin.Id, "LEGAL_ENTITY", null)).ShouldBeFalse(); // ACC-R1 violation
        (await Check(Org.Admin.Id, RoleAssignmentCreate, AccessPermissionChecker.ScopeRoleAssignmentBody, BodyContext("{not json"))).ShouldBeFalse();
        (await Check(Org.Admin.Id, RoleAssignmentCreate, AccessPermissionChecker.ScopeRoleAssignmentBody, BodyContext("{}"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Grant_LeavesTheBodyRewound_ForWhatComesNext()
    {
        var context = BodyContext($$"""{"scopeType":"LEGAL_ENTITY","scopeEntityId":"{{Org.LegalEntityA.Id}}"}""");

        (await Check(Org.UserA.Id, RoleAssignmentCreate, AccessPermissionChecker.ScopeRoleAssignmentBody, context)).ShouldBeTrue();
        context.Request.Body.Position.ShouldBe(0);
    }

    [Fact]
    public async Task Grant_WithANonSeekableBody_ThrowsAProgrammingError()
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new NonSeekableStream();

        await Should.ThrowAsync<InvalidOperationException>(
            () => Check(Org.Admin.Id, RoleAssignmentCreate, AccessPermissionChecker.ScopeRoleAssignmentBody, context));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}

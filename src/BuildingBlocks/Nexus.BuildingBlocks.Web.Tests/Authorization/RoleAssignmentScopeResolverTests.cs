using System.Text.Json;
using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Web.Authorization;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.Authorization;

/// <summary>
/// ACC-R4 scope resolution for <c>x-scope: body.scopeEntityId</c>: which
/// <c>access.has_permission</c> arguments each target scope type turns into. The effect of those
/// arguments on real assignments is proven in <see cref="AccessPermissionCheckerTests"/>.
/// </summary>
public sealed class RoleAssignmentScopeResolverTests(AccessPermissionDatabase db) : IClassFixture<AccessPermissionDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrgFixture Org => db.Org;

    private async Task<PermissionTarget?> Resolve(string? scopeType, Guid? scopeEntityId)
    {
        await using var connection = await db.Platform.OpenConnectionAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        return await RoleAssignmentScopeResolver.ResolveAsync(scopeType, scopeEntityId, connection, transaction, Ct);
    }

    private static void ShouldBeGlobalOnly(PermissionTarget? target)
    {
        var t = target.ShouldNotBeNull();
        t.LegalEntityId.ShouldNotBeNull();
        t.BusinessUnitId.ShouldBe(t.LegalEntityId);
        t.WarehouseId.ShouldBe(t.LegalEntityId);
        t.ProjectId.ShouldBe(t.LegalEntityId);
    }

    [Fact]
    public async Task Global_ResolvesToAFreshUnreachableId_EveryCall()
    {
        var first = await Resolve("GLOBAL", null);
        var second = await Resolve("GLOBAL", null);

        ShouldBeGlobalOnly(first);
        ShouldBeGlobalOnly(second);
        first!.Value.LegalEntityId.ShouldNotBe(second!.Value.LegalEntityId);
    }

    [Fact]
    public async Task LegalEntity_PassesOnlyTheLegalEntity()
    {
        (await Resolve("LEGAL_ENTITY", Org.LegalEntityA.Id))
            .ShouldBe(new PermissionTarget(Org.LegalEntityA.Id, null, null, null));
    }

    [Fact]
    public async Task BusinessUnit_AlsoPassesItsLegalEntity_SoGroupAndLegalEntityGrantsCanCover()
    {
        (await Resolve("BUSINESS_UNIT", Org.BusinessUnitA.Id))
            .ShouldBe(new PermissionTarget(Org.LegalEntityA.Id, Org.BusinessUnitA.Id, null, null));
    }

    [Fact]
    public async Task UnknownBusinessUnit_KeepsOnlyTheId()
    {
        var unknown = Guid.CreateVersion7();
        (await Resolve("BUSINESS_UNIT", unknown)).ShouldBe(new PermissionTarget(null, unknown, null, null));
    }

    [Fact]
    public async Task Warehouse_AlsoPassesItsBusinessUnitAndLegalEntity()
    {
        (await Resolve("WAREHOUSE", Org.WarehouseB))
            .ShouldBe(new PermissionTarget(Org.LegalEntityB.Id, Org.BusinessUnitB.Id, Org.WarehouseB, null));
    }

    [Fact]
    public async Task UnknownWarehouse_KeepsOnlyTheId()
    {
        var unknown = Guid.CreateVersion7();
        (await Resolve("WAREHOUSE", unknown)).ShouldBe(new PermissionTarget(null, null, unknown, null));
    }

    // Fail closed: any LE of the group would also satisfy has_permission's LEGAL_ENTITY clause,
    // letting a single-LE admin grant a group-wide role.
    [Fact]
    public async Task Group_IsCoveredOnlyByGlobal()
    {
        ShouldBeGlobalOnly(await Resolve("GROUP", Org.GroupId));
    }

    [Fact]
    public async Task Project_PassesOnlyTheProject()
    {
        var projectId = Guid.CreateVersion7();
        (await Resolve("PROJECT", projectId)).ShouldBe(new PermissionTarget(null, null, null, projectId));
    }

    [Theory]
    [InlineData("LEGAL_ENTITY")]
    [InlineData("BUSINESS_UNIT")]
    [InlineData("WAREHOUSE")]
    [InlineData("GROUP")]
    [InlineData("PROJECT")]
    public async Task NonGlobalWithoutEntityId_HasNoBasisToGrant(string scopeType)
    {
        (await Resolve(scopeType, null)).ShouldBeNull();
    }

    [Theory]
    [InlineData("TENANT")]
    [InlineData("legal_entity")]
    [InlineData(null)]
    public async Task UnknownScopeType_HasNoBasisToGrant(string? scopeType)
    {
        (await Resolve(scopeType, Org.LegalEntityA.Id)).ShouldBeNull();
    }

    [Fact]
    public void TryParse_ReadsCamelCaseBody()
    {
        var id = Guid.CreateVersion7();
        using var doc = JsonDocument.Parse($$"""{"userId":"{{Guid.CreateVersion7()}}","scopeType":"WAREHOUSE","scopeEntityId":"{{id}}"}""");

        RoleAssignmentScopeResolver.TryParse(doc.RootElement, out var scopeType, out var scopeEntityId).ShouldBeTrue();
        scopeType.ShouldBe("WAREHOUSE");
        scopeEntityId.ShouldBe(id);
    }

    [Fact]
    public void TryParse_MatchesNamesCaseInsensitively_LikeModelBinding()
    {
        using var doc = JsonDocument.Parse("""{"ScopeType":"GLOBAL","SCOPEENTITYID":null}""");

        RoleAssignmentScopeResolver.TryParse(doc.RootElement, out var scopeType, out var scopeEntityId).ShouldBeTrue();
        scopeType.ShouldBe("GLOBAL");
        scopeEntityId.ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"scopeEntityId":"0195f3a0-0000-7000-8000-000000000001"}""")]
    [InlineData("""{"scopeType":"LEGAL_ENTITY","scopeEntityId":"not-a-uuid"}""")]
    [InlineData("""{"scopeType":"LEGAL_ENTITY","scopeEntityId":42}""")]
    [InlineData("""{"scopeType":7}""")]
    [InlineData("""["GLOBAL"]""")]
    public void TryParse_RejectsMalformedBodies(string json)
    {
        using var doc = JsonDocument.Parse(json);

        RoleAssignmentScopeResolver.TryParse(doc.RootElement, out _, out _).ShouldBeFalse();
    }
}

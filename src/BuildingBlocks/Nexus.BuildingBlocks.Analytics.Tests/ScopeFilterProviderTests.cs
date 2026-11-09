using Nexus.BuildingBlocks.Analytics.Audit;
using Nexus.BuildingBlocks.Analytics.Scope;
using Nexus.BuildingBlocks.Persistence;
using Npgsql;
using Shouldly;

namespace Nexus.BuildingBlocks.Analytics.Tests;

/// <summary>
/// <see cref="ScopeFilterProvider"/> against the real <c>access</c> functions (conventions-api.md §7.2
/// and the audit scope decision of 2026-11-08), on a real request-style unit of work as
/// <c>svc_platform</c>.
/// </summary>
public sealed class ScopeFilterProviderTests(AnalyticsFixture fixture) : IClassFixture<AnalyticsFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> AllUsers => new() { "admin", "usera", "userb", "wha", "buviewer", "groupviewer" };

    [Theory(DisplayName = "§7.2: VisibleLegalEntitiesAsync returns exactly access.visible_legal_entities(), IncludeUnscoped false")]
    [MemberData(nameof(AllUsers))]
    public async Task VisibleLegalEntities_MatchesTheFunction(string user)
    {
        var userId = UserId(user);
        var expected = await fixture.Org.AsUser(userId, async connection =>
        {
            await using var command = new NpgsqlCommand("SELECT access.visible_legal_entities()", connection);
            return (Guid[])(await command.ExecuteScalarAsync(Ct))!;
        });

        await using var unitOfWork = await fixture.BeginAsync(userId);
        var filter = await new ScopeFilterProvider(unitOfWork).VisibleLegalEntitiesAsync(Ct);

        filter.LegalEntityIds.ShouldBe(expected.Order(), ignoreOrder: false);
        filter.IncludeUnscoped.ShouldBeFalse();
    }

    [Fact(DisplayName = "§7.2: concrete visible_legal_entities() sets (admin both LEs, usera LE-A, wha nothing)")]
    public async Task VisibleLegalEntities_ConcreteSets()
    {
        (await Visible(fixture.Org.Admin.Id)).LegalEntityIds.ShouldBe(new[] { fixture.Org.LegalEntityA.Id, fixture.Org.LegalEntityB.Id }, ignoreOrder: true);
        (await Visible(fixture.Org.UserA.Id)).LegalEntityIds.ShouldBe([fixture.Org.LegalEntityA.Id]);

        var wha = await Visible(fixture.Org.WhA.Id);
        wha.LegalEntityIds.ShouldBeEmpty();
        wha.IsEmpty.ShouldBeTrue();
    }

    [Fact(DisplayName = "Audit scope: admin (GLOBAL) gets every LE and the unscoped rows")]
    public async Task ForPermission_Global()
    {
        var filter = await ForAudit(fixture.Org.Admin.Id);
        filter.LegalEntityIds.ShouldBe(new[] { fixture.Org.LegalEntityA.Id, fixture.Org.LegalEntityB.Id }, ignoreOrder: true);
        filter.IncludeUnscoped.ShouldBeTrue();
    }

    [Fact(DisplayName = "Audit scope: usera (AUDIT:READ on LE-A) gets LE-A only, no unscoped rows")]
    public async Task ForPermission_LegalEntityScoped()
    {
        var filter = await ForAudit(fixture.Org.UserA.Id);
        filter.LegalEntityIds.ShouldBe([fixture.Org.LegalEntityA.Id]);
        filter.IncludeUnscoped.ShouldBeFalse();
    }

    [Fact(DisplayName = "Audit scope: a GROUP holder gets every LE of the group, but no unscoped rows (GLOBAL only)")]
    public async Task ForPermission_GroupScoped()
    {
        var filter = await ForAudit(fixture.GroupViewer);
        filter.LegalEntityIds.ShouldBe(new[] { fixture.Org.LegalEntityA.Id, fixture.Org.LegalEntityB.Id }, ignoreOrder: true);
        filter.IncludeUnscoped.ShouldBeFalse();
    }

    [Fact(DisplayName = "Audit scope (intended): AUDIT:READ held only at WAREHOUSE or BUSINESS_UNIT scope grants no legal entity → empty filter")]
    public async Task ForPermission_BelowLegalEntity_IsEmpty()
    {
        // has_permission(p, le) is false for a grant on a part of the legal entity only; event_log
        // rows are scoped by legal entity, so such a user sees no audit rows at all (same semantics
        // as the 0.6 legal-entity listing).
        foreach (var user in new[] { fixture.Org.WhA.Id, fixture.BuViewer })
        {
            var filter = await ForAudit(user);
            filter.LegalEntityIds.ShouldBeEmpty();
            filter.IncludeUnscoped.ShouldBeFalse();
            filter.IsEmpty.ShouldBeTrue();
        }
    }

    [Fact(DisplayName = "Audit scope: a permission the user lacks everywhere → empty filter, for admin too")]
    public async Task ForPermission_UnheldPermission_IsEmpty()
    {
        (await ForPermission(fixture.Org.UserA.Id, "NO_SUCH:READ")).IsEmpty.ShouldBeTrue();
        (await ForPermission(fixture.Org.Admin.Id, "NO_SUCH:READ")).IsEmpty.ShouldBeTrue();
    }

    [Fact(DisplayName = "Provider refuses to compute a scope outside an active request transaction")]
    public async Task Provider_RequiresActiveUnitOfWork()
    {
        var unitOfWork = await fixture.BeginAsync(fixture.Org.Admin.Id);
        await unitOfWork.RollbackAsync(Ct);
        var provider = new ScopeFilterProvider(unitOfWork);

        await Should.ThrowAsync<InvalidOperationException>(() => provider.VisibleLegalEntitiesAsync(Ct));
        await Should.ThrowAsync<InvalidOperationException>(() => provider.ForPermissionAsync(AuditPermissions.Read, Ct));
        await unitOfWork.DisposeAsync();
    }

    private Guid UserId(string user) => user switch
    {
        "admin" => fixture.Org.Admin.Id,
        "usera" => fixture.Org.UserA.Id,
        "userb" => fixture.Org.UserB.Id,
        "wha" => fixture.Org.WhA.Id,
        "buviewer" => fixture.BuViewer,
        "groupviewer" => fixture.GroupViewer,
        _ => throw new ArgumentOutOfRangeException(nameof(user)),
    };

    private async Task<ScopeFilter> Visible(Guid userId)
    {
        await using var unitOfWork = await fixture.BeginAsync(userId);
        return await new ScopeFilterProvider(unitOfWork).VisibleLegalEntitiesAsync(Ct);
    }

    private Task<ScopeFilter> ForAudit(Guid userId) => ForPermission(userId, AuditPermissions.Read);

    private async Task<ScopeFilter> ForPermission(Guid userId, string permission)
    {
        await using var unitOfWork = await fixture.BeginAsync(userId);
        return await new ScopeFilterProvider(unitOfWork).ForPermissionAsync(permission, Ct);
    }
}

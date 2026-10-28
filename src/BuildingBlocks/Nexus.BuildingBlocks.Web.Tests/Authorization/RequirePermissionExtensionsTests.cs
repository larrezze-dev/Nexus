using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.BuildingBlocks.Web.Authorization;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests.Authorization;

/// <summary>
/// <c>RequirePermission</c>/<c>WithScope</c>/<c>WithRules</c> only attach endpoint metadata and
/// require authorization; this builds a real (unstarted) <see cref="WebApplication"/>, maps one
/// endpoint with them, and inspects the actual <see cref="Endpoint"/> produced - the same
/// metadata the OpenAPI operation transformer (deliverable 3, not shipped in this step - see the
/// handback note) and <see cref="PermissionEndpointFilter"/> read.
/// </summary>
public sealed class RequirePermissionExtensionsTests
{
    private static readonly string[] ExpectedRuleIds = { "CAT-R2", "CAT-R3" };

    private static Endpoint MapTestEndpoint(out WebApplication app)
    {
        var builder = WebApplication.CreateBuilder();
        app = builder.Build();

        app.MapGet("/test", () => "ok")
            .RequirePermission("ASSET_TYPE", PermissionAction.Create)
            .WithScope("body.legalEntityId")
            .WithRules("CAT-R2", "CAT-R3");

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .Single();
    }

    [Fact]
    public void RequirePermission_Attaches_Permission_Metadata()
    {
        var endpoint = MapTestEndpoint(out _);

        var metadata = endpoint.Metadata.GetMetadata<PermissionMetadata>();

        metadata.ShouldNotBeNull();
        metadata.Resource.ShouldBe("ASSET_TYPE");
        metadata.Action.ShouldBe(PermissionAction.Create);
        metadata.Permission.ShouldBe("ASSET_TYPE:CREATE");
    }

    [Fact]
    public void RequirePermission_Requires_Authorization()
    {
        var endpoint = MapTestEndpoint(out _);

        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().ShouldNotBeNull();
    }

    [Fact]
    public void WithScope_Attaches_Scope_Metadata()
    {
        var endpoint = MapTestEndpoint(out _);

        endpoint.Metadata.GetMetadata<PermissionScopeMetadata>()!.Scope.ShouldBe("body.legalEntityId");
    }

    [Fact]
    public void WithRules_Attaches_Rule_Ids()
    {
        var endpoint = MapTestEndpoint(out _);

        endpoint.Metadata.GetMetadata<PermissionRulesMetadata>()!.RuleIds.ShouldBe(ExpectedRuleIds);
    }
}

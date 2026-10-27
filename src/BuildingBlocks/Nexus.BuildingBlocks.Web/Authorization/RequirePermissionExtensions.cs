using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Declares an endpoint's permission (conventions-backend.md §5):
/// <code>
/// group.MapPost("/", CreateAssetType.Handle)
///      .RequirePermission("ASSET_TYPE", PermissionAction.Create)
///      .WithScope("body.legalEntityId")
///      .WithRules("CAT-R2");
/// </code>
/// </summary>
public static class RequirePermissionExtensions
{
    /// <summary>
    /// Requires authentication (`RequireAuthorization()`, so a request with no/invalid token
    /// gets ASP.NET Core's own 401, conventions-backend.md §7) and attaches
    /// <see cref="PermissionMetadata"/> for later enforcement
    /// (<see cref="PermissionEndpointFilter"/>, gated by
    /// <see cref="NexusAuthorizationOptions.EnforcePermissions"/>) and for the OpenAPI
    /// `x-permission` extension (conventions-api.md §3).
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder,
        string resource,
        PermissionAction action)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.WithMetadata(new PermissionMetadata(resource, action));
        builder.AddEndpointFilter(PermissionEndpointFilter.InvokeAsync);

        return builder;
    }

    /// <summary>Declares where this operation's permission scope comes from (`body.legalEntityId`,
    /// `path.projectId`, `none`...), surfaced as `x-scope` in the OpenAPI document
    /// (conventions-api.md §3) and read by the real <see cref="IPermissionChecker"/> once step
    /// 0.6 enforces permissions.</summary>
    public static TBuilder WithScope<TBuilder>(this TBuilder builder, string scope)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new PermissionScopeMetadata(scope));

        return builder;
    }

    /// <summary>Declares the business-rule ids (`XXX-Rn`) this operation may return as a 422
    /// `code`, surfaced as `x-rules` in the OpenAPI document (conventions-api.md §3).</summary>
    public static TBuilder WithRules<TBuilder>(this TBuilder builder, params string[] ruleIds)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new PermissionRulesMetadata(ruleIds));

        return builder;
    }
}

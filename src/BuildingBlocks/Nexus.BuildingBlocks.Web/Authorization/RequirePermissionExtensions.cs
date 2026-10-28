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

    /// <summary>
    /// Marks the endpoint with the reserved `x-permission: PUBLIC` value (conventions-api.md
    /// §3): no authentication required at all. Calls <c>AllowAnonymous()</c> (so the endpoint
    /// is reachable even if a fallback authorization policy is ever configured) and attaches
    /// <see cref="PublicEndpointMetadata"/> for the OpenAPI operation transformer.
    /// </summary>
    public static TBuilder RequirePublic<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AllowAnonymous();
        builder.WithMetadata(new PublicEndpointMetadata());

        return builder;
    }

    /// <summary>
    /// Marks the endpoint with the reserved `x-permission: AUTHENTICATED` value
    /// (conventions-api.md §3): any valid session is enough, there is no `RESOURCE:ACTION`
    /// permission to check. Calls <c>RequireAuthorization()</c> (401 for no/invalid token) and
    /// attaches <see cref="AuthenticatedOnlyMetadata"/> for the OpenAPI operation transformer.
    /// Unlike <see cref="RequirePermission"/>, this never adds <see cref="PermissionEndpointFilter"/>
    /// - there is no permission to enforce.
    /// </summary>
    public static TBuilder RequireAuthenticatedOnly<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.WithMetadata(new AuthenticatedOnlyMetadata());

        return builder;
    }
}

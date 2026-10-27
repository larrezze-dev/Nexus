using Microsoft.AspNetCore.Http;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Decides whether the current request's caller holds a given permission. Only consulted when
/// <see cref="NexusAuthorizationOptions.EnforcePermissions"/> is <see langword="true"/>.
/// <see cref="AlwaysAllowPermissionChecker"/> is the only implementation registered by
/// <c>AddNexusAuthorization</c> in this step; step 0.6 replaces it (via DI) with one that calls
/// `access.has_permission(resource, action, legal_entity_id, warehouse_id, project_id)`
/// inside the request's transaction (domain/access.md §4, conventions-backend.md §5), reading
/// the scope from <see cref="PermissionScopeMetadata"/>.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(
        HttpContext httpContext,
        PermissionMetadata permission,
        PermissionScopeMetadata? scope,
        CancellationToken cancellationToken);
}

/// <summary>Default, step-0.5 implementation: always authorizes. Safe only because
/// <see cref="PermissionEndpointFilter"/> never calls it unless
/// <see cref="NexusAuthorizationOptions.EnforcePermissions"/> is explicitly turned on, which does
/// not happen before step 0.6.</summary>
internal sealed class AlwaysAllowPermissionChecker : IPermissionChecker
{
    public Task<bool> HasPermissionAsync(
        HttpContext httpContext,
        PermissionMetadata permission,
        PermissionScopeMetadata? scope,
        CancellationToken cancellationToken) => Task.FromResult(true);
}

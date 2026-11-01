using Microsoft.AspNetCore.Http;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Decides whether the current request's caller holds a given permission. Only consulted when
/// <see cref="NexusAuthorizationOptions.EnforcePermissions"/> is <see langword="true"/>.
/// <c>AddNexusAuthorization</c> registers <see cref="AccessPermissionChecker"/> (scoped), which
/// calls <c>access.has_permission</c> inside the request's transaction (domain/access.md §4,
/// conventions-backend.md §5), reading the scope from <see cref="PermissionScopeMetadata"/>.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(
        HttpContext httpContext,
        PermissionMetadata permission,
        PermissionScopeMetadata? scope,
        CancellationToken cancellationToken);
}

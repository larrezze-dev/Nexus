namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Bound from the <c>Nexus:Authorization</c> configuration section.
/// </summary>
public sealed class NexusAuthorizationOptions
{
    public const string SectionName = "Nexus:Authorization";

    /// <summary>
    /// When <see langword="false"/> (the default in step 0.5), <see cref="PermissionEndpointFilter"/>
    /// only relies on the authentication pipeline already run by `RequireAuthorization()`
    /// (401 if the caller is not authenticated) and lets every authenticated request through,
    /// regardless of its actual permissions. Step 0.6 turns this on and replaces
    /// <see cref="IPermissionChecker"/>'s default "always allow" implementation with a real one
    /// backed by `access.has_permission` (domain/access.md §4).
    /// </summary>
    public bool EnforcePermissions { get; set; }
}

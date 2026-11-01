namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Bound from the <c>Nexus:Authorization</c> configuration section.
/// </summary>
public sealed class NexusAuthorizationOptions
{
    public const string SectionName = "Nexus:Authorization";

    /// <summary>
    /// When <see langword="true"/>, <see cref="PermissionEndpointFilter"/> asks
    /// <see cref="IPermissionChecker"/> (<see cref="AccessPermissionChecker"/>, backed by
    /// `access.has_permission`, domain/access.md §4) and answers 403 when it denies. Services
    /// turn it on in their appsettings.json (nexus-platform since step 0.6). When
    /// <see langword="false"/> (the code default, kept so a host that forgets the setting does
    /// not need a database just to boot), the filter only relies on the 401 from
    /// `RequireAuthorization()` and lets every authenticated request through.
    /// </summary>
    public bool EnforcePermissions { get; set; }
}

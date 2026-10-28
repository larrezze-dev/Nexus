namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// The verb half of a permission (`RESOURCE:ACTION`, conventions-backend.md §5 /
/// conventions-api.md §3). Kept as an enum rather than a free string so
/// <c>RequirePermission("ASSET_TYPE", PermissionAction.Create)</c> cannot typo the action; its
/// <see cref="object.ToString"/> upper-cased is the exact token used in the permission string
/// and in the OpenAPI `x-permission` extension.
/// </summary>
public enum PermissionAction
{
    Create,
    Read,
    Update,

    /// <summary>State transitions (`POST /v1/x/{id}/&lt;verbo&gt;`, conventions-api.md §1) and
    /// other actions that are neither a plain create/read/update, e.g. `confirm`, `deactivate`.
    /// </summary>
    Execute,
}

using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Roles;

/// <summary>
/// Read side of a role's granted permissions (conventions-backend.md §1: Dapper on the unit of
/// work's connection and transaction; BE-R1). Requires an active transaction.
/// </summary>
public interface IRolePermissionReadDb
{
    /// <summary>
    /// <paramref name="roleId"/>'s currently-valid grants (<c>valid_to IS NULL</c>) as a
    /// <c>code</c> → <see cref="CurrentRoleGrant"/> map: everything <c>UpdateRolePermissions</c>'s
    /// ACC-R7 diff needs (the granted permission's id, to close the right row) and everything the
    /// unchanged entries of its response need (the original <c>validFrom</c>), without a second
    /// round trip after the write. Also used by <c>UpdateRole</c>/<c>DeactivateRole</c> for the
    /// <c>RoleUpdated</c> event's <c>permissionCodes</c> (<c>.Keys</c>).
    /// </summary>
    Task<IReadOnlyDictionary<string, CurrentRoleGrant>> GetCurrentGrantsAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>
    /// One page of <paramref name="roleId"/>'s grants valid at <paramref name="asOf"/> (A10:
    /// <c>valid_from &lt;= asOf AND (valid_to IS NULL OR valid_to &gt; asOf)</c>), ordered by
    /// <paramref name="sort"/> (fields of <see cref="PermissionGrantSortFields.Allowed"/>), then
    /// by code for a stable order. An empty <paramref name="sort"/> orders by code.
    /// </summary>
    Task<PagedResult<PermissionGrantItem>> ListGrantsAsync(
        Guid roleId, DateTimeOffset asOf, int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves each of <paramref name="codes"/> against the <c>access.permission</c> catalog.
    /// A code with no matching row is simply absent from the result; the caller
    /// (<c>UpdateRolePermissions</c>) detects and rejects unknown codes from that - and uses the
    /// returned <see cref="PermissionCatalogItem"/>s to build its response without a second read
    /// after the write.
    /// </summary>
    Task<IReadOnlyDictionary<string, PermissionCatalogItem>> ResolveAsync(
        IReadOnlyCollection<string> codes, CancellationToken cancellationToken);
}

/// <summary>A role's currently-valid grant of one permission (<see cref="IRolePermissionReadDb.GetCurrentGrantsAsync"/>).</summary>
public sealed record CurrentRoleGrant(Guid PermissionId, DateTimeOffset ValidFrom);

/// <summary>One row of the <c>access.permission</c> catalog (<see cref="IRolePermissionReadDb.ResolveAsync"/>).</summary>
public sealed record PermissionCatalogItem(
    Guid Id, string Code, string ResourceType, string Action, IReadOnlyDictionary<string, string> CaptionI18n);

/// <summary>Sortable fields of <c>GET /v1/roles/{id}/permissions</c> (API names).</summary>
public static class PermissionGrantSortFields
{
    public const string Code = "code";
    public const string ValidFrom = "validFrom";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, ValidFrom],
        StringComparer.Ordinal);
}

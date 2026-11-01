using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Permissions;

/// <summary>
/// Read side of the <c>access.permission</c> catalog (conventions-backend.md §1: Dapper on the
/// unit of work's connection and transaction; BE-R1). Seeded from <c>contracts/permissions.yaml</c>
/// (<c>db/seed/common</c>); there is no write path through the API.
/// </summary>
public interface IPermissionReadDb
{
    /// <summary>One page of the catalog ordered by <paramref name="sort"/> (fields of <see cref="PermissionSortFields.Allowed"/>), then by code.</summary>
    Task<PagedResult<PermissionItem>> ListAsync(int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);
}

/// <summary>One row of the catalog (<c>Permission</c> schema).</summary>
public sealed record PermissionItem(string Code, string ResourceType, string Action, IReadOnlyDictionary<string, string> CaptionI18n);

/// <summary>Sortable fields of <c>GET /v1/permissions</c> (API names).</summary>
public static class PermissionSortFields
{
    public const string Code = "code";
    public const string ResourceType = "resourceType";
    public const string Action = "action";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, ResourceType, Action],
        StringComparer.Ordinal);
}

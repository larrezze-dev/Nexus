using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>
/// Read side of role assignments (conventions-backend.md §1: Dapper on the unit of work's
/// connection and transaction; BE-R1). Returns flat read models, never aggregates. Requires an
/// active transaction.
/// </summary>
public interface IRoleAssignmentReadDb
{
    /// <summary>
    /// One page of role assignments ordered by <paramref name="sort"/> (fields of
    /// <see cref="RoleAssignmentSortFields.Allowed"/>), then by id for a stable order, filtered
    /// by <paramref name="filter"/>. An empty <paramref name="sort"/> orders by <c>validFrom</c>.
    /// </summary>
    Task<PagedResult<RoleAssignmentListItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, RoleAssignmentFilter filter, CancellationToken cancellationToken);
}

/// <summary>
/// <c>GET /v1/role-assignments</c> query parameters. <see cref="ActiveOn"/> is never
/// <see langword="null"/> by the time it reaches the read db: the query handler defaults it to
/// "now" (via <see cref="TimeProvider"/>) when the caller omits it - the endpoint always answers
/// "assignments active at this instant", never "every assignment ever granted".
/// </summary>
public sealed record RoleAssignmentFilter(Guid? UserId, Guid? RoleId, string? ScopeType, DateTimeOffset ActiveOn);

/// <summary>Sortable fields of <c>GET /v1/role-assignments</c> (API names).</summary>
public static class RoleAssignmentSortFields
{
    public const string ValidFrom = "validFrom";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [ValidFrom, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

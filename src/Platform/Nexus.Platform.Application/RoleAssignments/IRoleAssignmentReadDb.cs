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

    /// <summary>
    /// ACC-R2: whether <paramref name="scopeEntityId"/> is a row of the table that
    /// <paramref name="scopeType"/> names (<c>GROUP</c> → <c>org."group"</c>,
    /// <c>LEGAL_ENTITY</c> → <c>org.legal_entity</c>, <c>BUSINESS_UNIT</c> →
    /// <c>org.business_unit</c>, <c>WAREHOUSE</c> → <c>org.warehouse</c>). Read under the
    /// caller's RLS session, like every other read: a row the caller cannot see counts as
    /// missing. Any other scope type (<c>GLOBAL</c> has no entity, <c>PROJECT</c> is rejected
    /// earlier) answers <see langword="false"/>.
    /// </summary>
    Task<bool> ScopeEntityExistsAsync(string scopeType, Guid scopeEntityId, CancellationToken cancellationToken);
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

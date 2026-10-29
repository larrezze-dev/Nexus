using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Groups;

/// <summary>
/// Read side of groups (conventions-backend.md §1: Dapper on the unit of work's connection and
/// transaction; BE-R1). Returns flat read models, never aggregates. Requires an active
/// transaction.
/// </summary>
public interface IGroupReadDb
{
    Task<GroupDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of groups ordered by <paramref name="sort"/> (fields of
    /// <see cref="GroupSortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by code.
    /// </summary>
    Task<PagedResult<GroupListItem>> ListAsync(int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);
}

/// <summary>Sortable fields of <c>GET /v1/groups</c> (API names).</summary>
public static class GroupSortFields
{
    public const string Code = "code";
    public const string ReportingCurrencyCode = "reportingCurrencyCode";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, ReportingCurrencyCode, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

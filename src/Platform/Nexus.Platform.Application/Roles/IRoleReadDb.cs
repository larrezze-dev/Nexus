using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Roles;

/// <summary>
/// Read side of roles (conventions-backend.md §1: Dapper on the unit of work's connection and
/// transaction; BE-R1). Returns flat read models, never aggregates. Requires an active
/// transaction.
/// </summary>
public interface IRoleReadDb
{
    Task<RoleDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of roles ordered by <paramref name="sort"/> (fields of
    /// <see cref="RoleSortFields.Allowed"/>), then by id for a stable order, filtered by
    /// <paramref name="filter"/>. An empty <paramref name="sort"/> orders by code.
    /// </summary>
    Task<PagedResult<RoleListItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, RoleFilter filter, CancellationToken cancellationToken);
}

/// <summary><c>GET /v1/roles</c> query parameters (<c>q</c>, <c>isActive</c>).</summary>
public sealed record RoleFilter(string? Q, bool? IsActive);

/// <summary>Sortable fields of <c>GET /v1/roles</c> (API names).</summary>
public static class RoleSortFields
{
    public const string Code = "code";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

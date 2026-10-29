using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Locations;

/// <summary>
/// Read side of locations (conventions-backend.md §1: Dapper on the unit of work's connection
/// and transaction; BE-R1). Returns flat read models, never aggregates. Requires an active transaction.
/// </summary>
public interface ILocationReadDb
{
    Task<LocationDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of locations ordered by <paramref name="sort"/> (fields of
    /// <see cref="LocationSortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by name.
    /// </summary>
    Task<PagedResult<LocationListItem>> ListAsync(int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);
}

/// <summary>Sortable fields of <c>GET /v1/locations</c> (API names).</summary>
public static class LocationSortFields
{
    public const string Name = "name";
    public const string City = "city";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Name, City, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

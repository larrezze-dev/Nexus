using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Warehouses;

/// <summary>
/// Read side of warehouses (conventions-backend.md §1: Dapper on the unit of work's connection
/// and transaction; BE-R1). Returns flat read models, never aggregates. Requires an active transaction.
/// </summary>
public interface IWarehouseReadDb
{
    Task<WarehouseDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <b>Every</b> warehouse matching <paramref name="filter"/>, ordered by
    /// <paramref name="sort"/> (fields of <see cref="WarehouseSortFields.Allowed"/>), then by id
    /// for a stable order. An empty <paramref name="sort"/> orders by code.
    /// <para>
    /// Deliberately unpaginated: <c>listWarehouses</c> must first drop the rows whose scope the
    /// caller's <c>ORG_STRUCTURE:READ</c> does not cover (<c>org</c> has no RLS) and only then
    /// paginate, so <c>total</c> counts what the caller can see. <c>org.warehouse</c> is master
    /// data (hundreds of rows), not a high-volume table.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<WarehouseListItem>> ListAllAsync(
        IReadOnlyList<SortField> sort, WarehouseFilter filter, CancellationToken cancellationToken);
}

/// <summary><c>GET /v1/warehouses</c> query parameters (<c>q</c>, <c>legalEntityId</c>, <c>businessUnitId</c>, <c>isActive</c>).</summary>
public sealed record WarehouseFilter(string? Q, Guid? LegalEntityId, Guid? BusinessUnitId, bool? IsActive);

/// <summary>Sortable fields of <c>GET /v1/warehouses</c> (API names).</summary>
public static class WarehouseSortFields
{
    public const string Code = "code";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

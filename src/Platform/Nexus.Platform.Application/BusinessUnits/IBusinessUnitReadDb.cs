using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.BusinessUnits;

/// <summary>
/// Read side of business units (conventions-backend.md §1: Dapper on the unit of work's
/// connection and transaction; BE-R1). Returns flat read models, never aggregates. Requires an
/// active transaction.
/// </summary>
public interface IBusinessUnitReadDb
{
    Task<BusinessUnitDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <b>Every</b> business unit, ordered by <paramref name="sort"/> (fields of
    /// <see cref="BusinessUnitSortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by code.
    /// <para>
    /// Deliberately unpaginated: <c>listBusinessUnits</c> must first drop the rows whose scope
    /// the caller's <c>ORG_STRUCTURE:READ</c> does not cover (<c>org</c> has no RLS) and only
    /// then paginate, so <c>total</c> counts what the caller can see. <c>org.business_unit</c>
    /// is master data (tens to hundreds of rows), not a high-volume table.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<BusinessUnitListItem>> ListAllAsync(IReadOnlyList<SortField> sort, CancellationToken cancellationToken);

    /// <summary>
    /// The <c>legal_entity_id</c> of the business unit <paramref name="businessUnitId"/>, or
    /// <see langword="null"/> if it does not exist. Used by <c>Warehouses</c> create/update to
    /// check ORG-R3 (a cross-aggregate fact, so it lives on the read side rather than inside the
    /// pure <c>Warehouse</c> aggregate).
    /// </summary>
    Task<Guid?> FindLegalEntityIdAsync(Guid businessUnitId, CancellationToken cancellationToken);
}

/// <summary>Sortable fields of <c>GET /v1/business-units</c> (API names).</summary>
public static class BusinessUnitSortFields
{
    public const string Code = "code";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

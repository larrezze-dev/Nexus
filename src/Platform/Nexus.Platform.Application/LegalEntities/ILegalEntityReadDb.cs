using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.LegalEntities;

/// <summary>
/// Read side of legal entities (conventions-backend.md §1: Dapper on the unit of work's
/// connection and transaction; BE-R1). Returns flat read models, never aggregates. Requires an
/// active transaction.
/// </summary>
public interface ILegalEntityReadDb
{
    Task<LegalEntityDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of legal entities ordered by <paramref name="sort"/> (fields of
    /// <see cref="LegalEntitySortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by code.
    /// </summary>
    Task<PagedResult<LegalEntityListItem>> ListAsync(int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);

    /// <summary>
    /// Rule ORG-R5: <see langword="true"/> if <paramref name="legalEntityId"/> has at least one
    /// active <c>BusinessUnit</c> or <c>Warehouse</c> (<c>EXISTS</c>, no row materialised).
    /// </summary>
    Task<bool> HasActiveChildrenAsync(Guid legalEntityId, CancellationToken cancellationToken);
}

/// <summary>Sortable fields of <c>GET /v1/legal-entities</c> (API names).</summary>
public static class LegalEntitySortFields
{
    public const string Code = "code";
    public const string CurrencyCode = "currencyCode";
    public const string CountryCode = "countryCode";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, CurrencyCode, CountryCode, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

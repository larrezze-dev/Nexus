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
    /// <b>Every</b> legal entity, ordered by <paramref name="sort"/> (fields of
    /// <see cref="LegalEntitySortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by code.
    /// <para>
    /// Deliberately unpaginated: <c>listLegalEntities</c> must first drop the rows whose scope
    /// the caller's <c>ORG_STRUCTURE:READ</c> does not cover (<c>org</c> has no RLS) and only
    /// then paginate, so <c>total</c> counts what the caller can see. <c>org.legal_entity</c>
    /// holds tens of rows.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<LegalEntityListItem>> ListAllAsync(IReadOnlyList<SortField> sort, CancellationToken cancellationToken);

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

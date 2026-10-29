using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Currencies;

/// <summary>
/// Read side of currencies (conventions-backend.md §1: Dapper on the unit of work's connection
/// and transaction; BE-R1). Returns flat read models, never aggregates. Requires an active
/// transaction.
/// </summary>
public interface ICurrencyReadDb
{
    Task<CurrencyDetails?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// One page of currencies ordered by <paramref name="sort"/> (fields of
    /// <see cref="CurrencySortFields.Allowed"/>), then by code for a stable order. An empty
    /// <paramref name="sort"/> orders by code.
    /// </summary>
    Task<PagedResult<CurrencyListItem>> ListAsync(int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);

    /// <summary>
    /// Rule ORG-R7's guard query: whether <paramref name="code"/> is still the reporting
    /// currency of any <c>Group</c> or the currency of any active <c>LegalEntity</c>.
    /// </summary>
    Task<bool> IsReferencedByActiveEntityAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Rule ORG-R7's other guard query (Group/LegalEntity create/update, before saving):
    /// <see langword="true"/> only if <paramref name="code"/> exists and is active.
    /// </summary>
    Task<bool> IsActiveAsync(string code, CancellationToken cancellationToken);
}

/// <summary>Sortable fields of <c>GET /v1/currencies</c> (API names).</summary>
public static class CurrencySortFields
{
    public const string Code = "code";
    public const string MinorUnits = "minorUnits";
    public const string IsActive = "isActive";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [Code, MinorUnits, IsActive],
        StringComparer.Ordinal);
}

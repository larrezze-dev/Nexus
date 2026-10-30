using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Parties;

/// <summary>
/// Read side of parties (conventions-backend.md §1: Dapper on the unit of work's connection and
/// transaction; BE-R1). Returns flat read models, never aggregates. Requires an active
/// transaction.
/// </summary>
public interface IPartyReadDb
{
    Task<PartyDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of parties ordered by <paramref name="sort"/> (fields of
    /// <see cref="PartySortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by legal name. <paramref name="partyType"/> and
    /// <paramref name="isInternal"/> filter exactly when given; <paramref name="q"/> matches
    /// <c>legal_name</c> (<c>ix_party_legal_name_trgm</c>).
    /// </summary>
    Task<PagedResult<PartyListItem>> ListAsync(
        int page,
        int pageSize,
        IReadOnlyList<SortField> sort,
        string? partyType,
        bool? isInternal,
        string? q,
        CancellationToken cancellationToken);
}

/// <summary>Sortable fields of <c>GET /v1/parties</c> (API names).</summary>
public static class PartySortFields
{
    public const string LegalName = "legalName";
    public const string PartyType = "partyType";
    public const string IsInternal = "isInternal";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [LegalName, PartyType, IsInternal, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

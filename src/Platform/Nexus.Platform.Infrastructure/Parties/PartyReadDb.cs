using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Parties;

namespace Nexus.Platform.Infrastructure.Parties;

/// <summary>
/// <see cref="IPartyReadDb"/> with Dapper on the unit of work's own connection and transaction
/// (conventions-backend.md §1, BE-R1). Every value is a parameter; the only SQL fragments that
/// vary are ORDER BY expressions and the WHERE clauses for the optional filters, all taken from
/// fixed whitelists/flags, never from request text.
/// </summary>
internal sealed class PartyReadDb : IPartyReadDb
{
    private const string DetailColumns =
        """
        id AS Id, party_type AS PartyType, legal_name AS LegalName, display_name AS DisplayName,
        tax_id AS TaxId, erp_company_code AS ErpCompanyCode, is_internal AS IsInternal,
        is_active AS IsActive, created_at AS CreatedAt, updated_at AS UpdatedAt,
        xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [PartySortFields.LegalName] = "legal_name",
        [PartySortFields.PartyType] = "party_type",
        [PartySortFields.IsInternal] = "is_internal",
        [PartySortFields.IsActive] = "is_active",
        [PartySortFields.CreatedAt] = "created_at",
        [PartySortFields.UpdatedAt] = "updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public PartyReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PartyDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.party WHERE id = @id",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<PartyListItem>> ListAsync(
        int page,
        int pageSize,
        IReadOnlyList<SortField> sort,
        string? partyType,
        bool? isInternal,
        string? q,
        CancellationToken cancellationToken)
    {
        var where = BuildWhere(partyType, isInternal, q);
        var parameters = new
        {
            partyType,
            isInternal,
            q = q is null ? null : "%" + q + "%",
            limit = pageSize,
            offset = (long)(page - 1) * pageSize,
        };

        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            $"SELECT count(*) FROM org.party{where}",
            parameters,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[PartySortFields.LegalName]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT id AS Id, party_type AS PartyType, legal_name AS LegalName, display_name AS DisplayName,
                    is_internal AS IsInternal, is_active AS IsActive
             FROM org.party{where}
             ORDER BY {orderBy}, id
             LIMIT @limit OFFSET @offset
             """,
            parameters,
            cancellationToken));

        var items = rows
            .Select(r => new PartyListItem(r.Id, r.PartyType, r.LegalName, r.DisplayName, r.IsInternal, r.IsActive))
            .ToList();

        return new PagedResult<PartyListItem>(items, page, pageSize, total);
    }

    private static string BuildWhere(string? partyType, bool? isInternal, string? q)
    {
        var clauses = new List<string>();
        if (partyType is not null)
        {
            clauses.Add("party_type = @partyType");
        }

        if (isInternal is not null)
        {
            clauses.Add("is_internal = @isInternal");
        }

        if (q is not null)
        {
            clauses.Add("legal_name ILIKE @q");
        }

        return clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    // Dapper row types: settable properties (lenient mapping), UTC DateTime for timestamptz.
    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public string PartyType { get; set; } = string.Empty;

        public string LegalName { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public string? TaxId { get; set; }

        public string? ErpCompanyCode { get; set; }

        public bool IsInternal { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public PartyDetails ToDetails() => new(
            Id, PartyType, LegalName, DisplayName, TaxId, ErpCompanyCode, IsInternal, IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            new PartyVersion(checked((uint)Version)));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public string PartyType { get; set; } = string.Empty;

        public string LegalName { get; set; } = string.Empty;

        public string? DisplayName { get; set; }

        public bool IsInternal { get; set; }

        public bool IsActive { get; set; }
    }
}

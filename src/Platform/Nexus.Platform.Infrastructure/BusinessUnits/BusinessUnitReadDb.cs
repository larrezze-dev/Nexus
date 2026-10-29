using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.BusinessUnits;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.BusinessUnits;

/// <summary>
/// <see cref="IBusinessUnitReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1).
/// </summary>
internal sealed class BusinessUnitReadDb : IBusinessUnitReadDb
{
    private const string DetailColumns =
        """
        b.id AS Id, b.party_id AS PartyId, b.legal_entity_id AS LegalEntityId, b.code AS Code,
        b.caption_i18n::text AS CaptionI18nJson, b.cost_center_code AS CostCenterCode,
        b.is_active AS IsActive, b.created_at AS CreatedAt, b.updated_at AS UpdatedAt,
        b.xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [BusinessUnitSortFields.Code] = "b.code",
        [BusinessUnitSortFields.IsActive] = "b.is_active",
        [BusinessUnitSortFields.CreatedAt] = "b.created_at",
        [BusinessUnitSortFields.UpdatedAt] = "b.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public BusinessUnitReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<BusinessUnitDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.business_unit b WHERE b.id = @id",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<BusinessUnitListItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            "SELECT count(*) FROM org.business_unit",
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[BusinessUnitSortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT b.id AS Id, b.legal_entity_id AS LegalEntityId, b.code AS Code,
                    b.caption_i18n::text AS CaptionI18nJson, b.is_active AS IsActive
             FROM org.business_unit b
             ORDER BY {orderBy}, b.id
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows
            .Select(r => new BusinessUnitListItem(r.Id, r.LegalEntityId, r.Code, I18nJson.Parse(r.CaptionI18nJson), r.IsActive))
            .ToList();

        return new PagedResult<BusinessUnitListItem>(items, page, pageSize, total);
    }

    public Task<Guid?> FindLegalEntityIdAsync(Guid businessUnitId, CancellationToken cancellationToken) =>
        _unitOfWork.Connection.ExecuteScalarAsync<Guid?>(Command(
            "SELECT legal_entity_id FROM org.business_unit WHERE id = @businessUnitId",
            new { businessUnitId },
            cancellationToken));

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public Guid PartyId { get; set; }

        public Guid LegalEntityId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public string? CostCenterCode { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public BusinessUnitDetails ToDetails() => new(
            Id, PartyId, LegalEntityId, Code, I18nJson.Parse(CaptionI18nJson), CostCenterCode, IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            checked((uint)Version));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public Guid LegalEntityId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public bool IsActive { get; set; }
    }
}

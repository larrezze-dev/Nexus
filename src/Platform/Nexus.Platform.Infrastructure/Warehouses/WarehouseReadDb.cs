using System.Text;
using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Warehouses;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Warehouses;

/// <summary>
/// <see cref="IWarehouseReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1). The <see cref="WarehouseFilter"/> clauses are
/// appended to a fixed <c>WHERE</c> skeleton; every filter value is a parameter, never
/// concatenated into the SQL text.
/// </summary>
internal sealed class WarehouseReadDb : IWarehouseReadDb
{
    private const string DetailColumns =
        """
        w.id AS Id, w.location_id AS LocationId, w.legal_entity_id AS LegalEntityId,
        w.business_unit_id AS BusinessUnitId, w.code AS Code, w.caption_i18n::text AS CaptionI18nJson,
        w.is_active AS IsActive, w.created_at AS CreatedAt, w.updated_at AS UpdatedAt,
        w.xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [WarehouseSortFields.Code] = "w.code",
        [WarehouseSortFields.IsActive] = "w.is_active",
        [WarehouseSortFields.CreatedAt] = "w.created_at",
        [WarehouseSortFields.UpdatedAt] = "w.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public WarehouseReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<WarehouseDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.warehouse w WHERE w.id = @id",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<WarehouseListItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, WarehouseFilter filter, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildWhere(filter);

        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            $"SELECT count(*) FROM org.warehouse w {where}",
            parameters,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[WarehouseSortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        parameters.Add("limit", pageSize);
        parameters.Add("offset", (long)(page - 1) * pageSize);

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT w.id AS Id, w.legal_entity_id AS LegalEntityId, w.business_unit_id AS BusinessUnitId,
                    w.code AS Code, w.caption_i18n::text AS CaptionI18nJson, w.is_active AS IsActive
             FROM org.warehouse w
             {where}
             ORDER BY {orderBy}, w.id
             LIMIT @limit OFFSET @offset
             """,
            parameters,
            cancellationToken));

        var items = rows
            .Select(r => new WarehouseListItem(r.Id, r.LegalEntityId, r.BusinessUnitId, r.Code, I18nJson.Parse(r.CaptionI18nJson), r.IsActive))
            .ToList();

        return new PagedResult<WarehouseListItem>(items, page, pageSize, total);
    }

    private static (string Where, DynamicParameters Parameters) BuildWhere(WarehouseFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            clauses.Add("(w.code ILIKE @q OR w.caption_i18n::text ILIKE @q)");
            parameters.Add("q", "%" + filter.Q + "%");
        }

        if (filter.LegalEntityId is { } legalEntityId)
        {
            clauses.Add("w.legal_entity_id = @legalEntityId");
            parameters.Add("legalEntityId", legalEntityId);
        }

        if (filter.BusinessUnitId is { } businessUnitId)
        {
            clauses.Add("w.business_unit_id = @businessUnitId");
            parameters.Add("businessUnitId", businessUnitId);
        }

        if (filter.IsActive is { } isActive)
        {
            clauses.Add("w.is_active = @isActive");
            parameters.Add("isActive", isActive);
        }

        if (clauses.Count == 0)
        {
            return (string.Empty, parameters);
        }

        var where = new StringBuilder("WHERE ").Append(string.Join(" AND ", clauses));
        return (where.ToString(), parameters);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public Guid LocationId { get; set; }

        public Guid LegalEntityId { get; set; }

        public Guid? BusinessUnitId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public WarehouseDetails ToDetails() => new(
            Id, LocationId, LegalEntityId, BusinessUnitId, Code, I18nJson.Parse(CaptionI18nJson), IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            checked((uint)Version));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public Guid LegalEntityId { get; set; }

        public Guid? BusinessUnitId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public bool IsActive { get; set; }
    }
}

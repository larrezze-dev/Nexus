using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.LegalEntities;

namespace Nexus.Platform.Infrastructure.LegalEntities;

/// <summary>
/// <see cref="ILegalEntityReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1). Every value is a parameter; the only SQL
/// fragments that vary are ORDER BY expressions taken from the fixed <see cref="SortColumns"/>
/// whitelist.
/// </summary>
internal sealed class LegalEntityReadDb : ILegalEntityReadDb
{
    private const string DetailColumns =
        """
        le.id AS Id, le.party_id AS PartyId, le.group_id AS GroupId, le.code AS Code,
        le.currency_code AS CurrencyCode, le.country_code AS CountryCode, le.is_active AS IsActive,
        le.created_at AS CreatedAt, le.updated_at AS UpdatedAt, le.xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [LegalEntitySortFields.Code] = "le.code",
        [LegalEntitySortFields.CurrencyCode] = "le.currency_code",
        [LegalEntitySortFields.CountryCode] = "le.country_code",
        [LegalEntitySortFields.IsActive] = "le.is_active",
        [LegalEntitySortFields.CreatedAt] = "le.created_at",
        [LegalEntitySortFields.UpdatedAt] = "le.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public LegalEntityReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<LegalEntityDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"SELECT {DetailColumns} FROM org.legal_entity le WHERE le.id = @id",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<LegalEntityListItem>> ListAsync(
        int page,
        int pageSize,
        IReadOnlyList<SortField> sort,
        CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            "SELECT count(*) FROM org.legal_entity",
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[LegalEntitySortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT le.id AS Id, le.group_id AS GroupId, le.code AS Code, le.currency_code AS CurrencyCode,
                    le.country_code AS CountryCode, le.is_active AS IsActive
             FROM org.legal_entity le
             ORDER BY {orderBy}, le.id
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows
            .Select(r => new LegalEntityListItem(r.Id, r.GroupId, r.Code, r.CurrencyCode, r.CountryCode, r.IsActive))
            .ToList();

        return new PagedResult<LegalEntityListItem>(items, page, pageSize, total);
    }

    public async Task<bool> HasActiveChildrenAsync(Guid legalEntityId, CancellationToken cancellationToken) =>
        await _unitOfWork.Connection.ExecuteScalarAsync<bool>(Command(
            """
            SELECT EXISTS (SELECT 1 FROM org.business_unit WHERE legal_entity_id = @legalEntityId AND is_active)
                OR EXISTS (SELECT 1 FROM org.warehouse WHERE legal_entity_id = @legalEntityId AND is_active)
            """,
            new { legalEntityId },
            cancellationToken));

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    // Dapper row types: settable properties (lenient mapping), UTC DateTime for timestamptz.
    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public Guid PartyId { get; set; }

        public Guid GroupId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CurrencyCode { get; set; } = string.Empty;

        public string CountryCode { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public LegalEntityDetails ToDetails() => new(
            Id, PartyId, GroupId, Code, CurrencyCode, CountryCode, IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            checked((uint)Version));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public Guid GroupId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CurrencyCode { get; set; } = string.Empty;

        public string CountryCode { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}

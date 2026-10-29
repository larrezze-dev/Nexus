using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Groups;

namespace Nexus.Platform.Infrastructure.Groups;

/// <summary>
/// <see cref="IGroupReadDb"/> with Dapper on the unit of work's own connection and transaction
/// (conventions-backend.md §1, BE-R1). Every value is a parameter; the only SQL fragments that
/// vary are ORDER BY expressions taken from the fixed <see cref="SortColumns"/> whitelist.
/// </summary>
internal sealed class GroupReadDb : IGroupReadDb
{
    private const string DetailColumns =
        """
        g.id AS Id, g.party_id AS PartyId, g.code AS Code, g.consolidation_method AS ConsolidationMethod,
        g.reporting_currency_code AS ReportingCurrencyCode, g.created_at AS CreatedAt, g.updated_at AS UpdatedAt,
        g.xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [GroupSortFields.Code] = "g.code",
        [GroupSortFields.ReportingCurrencyCode] = "g.reporting_currency_code",
        [GroupSortFields.CreatedAt] = "g.created_at",
        [GroupSortFields.UpdatedAt] = "g.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public GroupReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<GroupDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"""SELECT {DetailColumns} FROM org."group" g WHERE g.id = @id""",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<GroupListItem>> ListAsync(
        int page,
        int pageSize,
        IReadOnlyList<SortField> sort,
        CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            """SELECT count(*) FROM org."group" """,
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[GroupSortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT g.id AS Id, g.party_id AS PartyId, g.code AS Code, g.reporting_currency_code AS ReportingCurrencyCode
             FROM org."group" g
             ORDER BY {orderBy}, g.id
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows
            .Select(r => new GroupListItem(r.Id, r.PartyId, r.Code, r.ReportingCurrencyCode))
            .ToList();

        return new PagedResult<GroupListItem>(items, page, pageSize, total);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    // Dapper row types: settable properties (lenient mapping), UTC DateTime for timestamptz.
    private sealed class DetailRow
    {
        public Guid Id { get; set; }

        public Guid PartyId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string ConsolidationMethod { get; set; } = string.Empty;

        public string ReportingCurrencyCode { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public GroupDetails ToDetails() => new(
            Id, PartyId, Code, ConsolidationMethod, ReportingCurrencyCode,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            checked((uint)Version));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public Guid PartyId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string ReportingCurrencyCode { get; set; } = string.Empty;
    }
}

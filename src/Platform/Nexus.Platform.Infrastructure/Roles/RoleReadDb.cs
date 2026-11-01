using System.Text;
using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Roles;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Roles;

/// <summary>
/// <see cref="IRoleReadDb"/> with Dapper on the unit of work's own connection and transaction
/// (conventions-backend.md §1, BE-R1). The <see cref="RoleFilter"/> clauses are appended to a
/// fixed <c>WHERE</c> skeleton; every filter value is a parameter, never concatenated into the
/// SQL text.
/// </summary>
internal sealed class RoleReadDb : IRoleReadDb
{
    private const string DetailColumns =
        """
        r.id AS Id, r.code AS Code, r.caption_i18n::text AS CaptionI18nJson, r.description AS Description,
        r.is_system AS IsSystem, r.is_active AS IsActive, r.created_at AS CreatedAt, r.updated_at AS UpdatedAt,
        r.xmin::text::bigint AS Version
        """;

    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [RoleSortFields.Code] = "r.code",
        [RoleSortFields.IsActive] = "r.is_active",
        [RoleSortFields.CreatedAt] = "r.created_at",
        [RoleSortFields.UpdatedAt] = "r.updated_at",
    };

    private readonly IUnitOfWork _unitOfWork;

    public RoleReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<RoleDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _unitOfWork.Connection.QuerySingleOrDefaultAsync<DetailRow>(Command(
            $"""SELECT {DetailColumns} FROM access.role r WHERE r.id = @id""",
            new { id },
            cancellationToken));

        return row?.ToDetails();
    }

    public async Task<PagedResult<RoleListItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, RoleFilter filter, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildWhere(filter);

        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            $"""SELECT count(*) FROM access.role r {where}""",
            parameters,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[RoleSortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        parameters.Add("limit", pageSize);
        parameters.Add("offset", (long)(page - 1) * pageSize);

        var rows = await _unitOfWork.Connection.QueryAsync<ListRow>(Command(
            $"""
             SELECT r.id AS Id, r.code AS Code, r.caption_i18n::text AS CaptionI18nJson, r.is_system AS IsSystem, r.is_active AS IsActive
             FROM access.role r
             {where}
             ORDER BY {orderBy}, r.id
             LIMIT @limit OFFSET @offset
             """,
            parameters,
            cancellationToken));

        var items = rows
            .Select(r => new RoleListItem(r.Id, r.Code, I18nJson.Parse(r.CaptionI18nJson), r.IsSystem, r.IsActive))
            .ToList();

        return new PagedResult<RoleListItem>(items, page, pageSize, total);
    }

    private static (string Where, DynamicParameters Parameters) BuildWhere(RoleFilter filter)
    {
        var clauses = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            clauses.Add("(r.code ILIKE @q OR r.caption_i18n::text ILIKE @q)");
            parameters.Add("q", "%" + filter.Q + "%");
        }

        if (filter.IsActive is { } isActive)
        {
            clauses.Add("r.is_active = @isActive");
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

        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public string? Description { get; set; }

        public bool IsSystem { get; set; }

        public bool IsActive { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        public long Version { get; set; }

        public RoleDetails ToDetails() => new(
            Id, Code, I18nJson.Parse(CaptionI18nJson), Description, IsSystem, IsActive,
            DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc),
            checked((uint)Version));
    }

    private sealed class ListRow
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public bool IsSystem { get; set; }

        public bool IsActive { get; set; }
    }
}

using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Roles;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Roles;

/// <summary>
/// <see cref="IRolePermissionReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1).
/// </summary>
internal sealed class RolePermissionReadDb : IRolePermissionReadDb
{
    private readonly IUnitOfWork _unitOfWork;

    public RolePermissionReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyDictionary<string, CurrentRoleGrant>> GetCurrentGrantsAsync(Guid roleId, CancellationToken cancellationToken)
    {
        var rows = await _unitOfWork.Connection.QueryAsync<CurrentGrantRow>(Command(
            """
            SELECT p.code AS Code, rp.permission_id AS PermissionId, rp.valid_from AS ValidFrom
            FROM access.role_permission rp
            JOIN access.permission p ON p.id = rp.permission_id
            WHERE rp.role_id = @roleId AND rp.valid_to IS NULL
            """,
            new { roleId },
            cancellationToken));

        return rows.ToDictionary(
            r => r.Code,
            r => new CurrentRoleGrant(r.PermissionId, DateTime.SpecifyKind(r.ValidFrom, DateTimeKind.Utc)),
            StringComparer.Ordinal);
    }

    public async Task<PagedResult<PermissionGrantItem>> ListGrantsAsync(
        Guid roleId, DateTimeOffset asOf, int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("roleId", roleId);
        parameters.Add("asOf", asOf);

        const string Where =
            "WHERE rp.role_id = @roleId AND rp.valid_from <= @asOf AND (rp.valid_to IS NULL OR rp.valid_to > @asOf)";

        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            $"""
             SELECT count(*) FROM access.role_permission rp
             {Where}
             """,
            parameters,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? "p.code"
            : string.Join(", ", sort.Select(f => SortColumn(f.Field) + (f.Descending ? " DESC" : " ASC")));

        parameters.Add("limit", pageSize);
        parameters.Add("offset", (long)(page - 1) * pageSize);

        var rows = await _unitOfWork.Connection.QueryAsync<GrantRow>(Command(
            $"""
             SELECT p.code AS Code, p.resource_type AS ResourceType, p.action AS Action, p.caption_i18n::text AS CaptionI18nJson,
                    rp.valid_from AS ValidFrom, rp.valid_to AS ValidTo
             FROM access.role_permission rp
             JOIN access.permission p ON p.id = rp.permission_id
             {Where}
             ORDER BY {orderBy}, p.code
             LIMIT @limit OFFSET @offset
             """,
            parameters,
            cancellationToken));

        var items = rows.Select(r => r.ToItem()).ToList();
        return new PagedResult<PermissionGrantItem>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyDictionary<string, PermissionCatalogItem>> ResolveAsync(
        IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        if (codes.Count == 0)
        {
            return new Dictionary<string, PermissionCatalogItem>(StringComparer.Ordinal);
        }

        var rows = await _unitOfWork.Connection.QueryAsync<CatalogRow>(Command(
            """
            SELECT id AS Id, code AS Code, resource_type AS ResourceType, action AS Action, caption_i18n::text AS CaptionI18nJson
            FROM access.permission
            WHERE code = ANY(@codes)
            """,
            new { codes = codes.ToArray() },
            cancellationToken));

        return rows.ToDictionary(
            r => r.Code,
            r => new PermissionCatalogItem(r.Id, r.Code, r.ResourceType, r.Action, I18nJson.Parse(r.CaptionI18nJson)),
            StringComparer.Ordinal);
    }

    private static string SortColumn(string field) => field switch
    {
        PermissionGrantSortFields.ValidFrom => "rp.valid_from",
        _ => "p.code",
    };

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class CurrentGrantRow
    {
        public string Code { get; set; } = string.Empty;

        public Guid PermissionId { get; set; }

        public DateTime ValidFrom { get; set; }
    }

    private sealed class GrantRow
    {
        public string Code { get; set; } = string.Empty;

        public string ResourceType { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";

        public DateTime ValidFrom { get; set; }

        public DateTime? ValidTo { get; set; }

        public PermissionGrantItem ToItem() => new(
            Code, ResourceType, Action, I18nJson.Parse(CaptionI18nJson),
            DateTime.SpecifyKind(ValidFrom, DateTimeKind.Utc),
            ValidTo is { } vt ? DateTime.SpecifyKind(vt, DateTimeKind.Utc) : null);
    }

    private sealed class CatalogRow
    {
        public Guid Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string ResourceType { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";
    }
}

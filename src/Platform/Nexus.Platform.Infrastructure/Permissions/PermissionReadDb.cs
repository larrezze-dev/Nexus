using Dapper;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Platform.Application.Permissions;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Permissions;

/// <summary>
/// <see cref="IPermissionReadDb"/> with Dapper on the unit of work's own connection and
/// transaction (conventions-backend.md §1, BE-R1).
/// </summary>
internal sealed class PermissionReadDb : IPermissionReadDb
{
    private static readonly Dictionary<string, string> SortColumns = new(StringComparer.Ordinal)
    {
        [PermissionSortFields.Code] = "code",
        [PermissionSortFields.ResourceType] = "resource_type",
        [PermissionSortFields.Action] = "action",
    };

    private readonly IUnitOfWork _unitOfWork;

    public PermissionReadDb(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<PermissionItem>> ListAsync(
        int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken)
    {
        var total = await _unitOfWork.Connection.ExecuteScalarAsync<long>(Command(
            "SELECT count(*) FROM access.permission",
            null,
            cancellationToken));

        var orderBy = sort.Count == 0
            ? SortColumns[PermissionSortFields.Code]
            : string.Join(", ", sort.Select(f => SortColumns[f.Field] + (f.Descending ? " DESC" : " ASC")));

        var rows = await _unitOfWork.Connection.QueryAsync<Row>(Command(
            $"""
             SELECT code AS Code, resource_type AS ResourceType, action AS Action, caption_i18n::text AS CaptionI18nJson
             FROM access.permission
             ORDER BY {orderBy}, code
             LIMIT @limit OFFSET @offset
             """,
            new { limit = pageSize, offset = (long)(page - 1) * pageSize },
            cancellationToken));

        var items = rows.Select(r => new PermissionItem(r.Code, r.ResourceType, r.Action, I18nJson.Parse(r.CaptionI18nJson))).ToList();
        return new PagedResult<PermissionItem>(items, page, pageSize, total);
    }

    private CommandDefinition Command(string sql, object? parameters, CancellationToken cancellationToken) =>
        new(sql, parameters, _unitOfWork.Transaction, cancellationToken: cancellationToken);

    private sealed class Row
    {
        public string Code { get; set; } = string.Empty;

        public string ResourceType { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;

        public string CaptionI18nJson { get; set; } = "{}";
    }
}

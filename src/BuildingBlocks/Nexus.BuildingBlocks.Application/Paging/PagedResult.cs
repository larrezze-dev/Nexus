namespace Nexus.BuildingBlocks.Application.Paging;

/// <summary>
/// One page of an offset-paginated master-data listing (conventions-api.md §4), serialized as
/// the shared <c>Page</c> schema: <c>{ "items": [...], "page": 1, "pageSize": 50, "total": 1234 }</c>.
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

/// <summary>Offset paging limits (conventions-api.md §4; <c>_shared/components.yaml</c> Page/PageSize).</summary>
public static class PageLimits
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;
}

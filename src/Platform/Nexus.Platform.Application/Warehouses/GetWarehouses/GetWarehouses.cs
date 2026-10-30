using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Warehouses.GetWarehouses;

/// <summary>
/// <c>GET /v1/warehouses</c> (<c>listWarehouses</c>): offset pagination (conventions-api.md §4)
/// with the contract's <c>page</c>, <c>pageSize</c> (max 200), <c>sort</c>
/// (<see cref="WarehouseSortFields.Allowed"/>), free-text <c>q</c> and the
/// <c>legalEntityId</c>/<c>businessUnitId</c>/<c>isActive</c> filters.
/// </summary>
public sealed record GetWarehousesQuery(
    int Page,
    int PageSize,
    string? Sort,
    string? Q,
    Guid? LegalEntityId,
    Guid? BusinessUnitId,
    bool? IsActive) : IQuery<PagedResult<WarehouseListItem>>;

internal sealed class GetWarehousesValidator : AbstractValidator<GetWarehousesQuery>
{
    public GetWarehousesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, WarehouseSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetWarehousesHandler : IQueryHandler<GetWarehousesQuery, PagedResult<WarehouseListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseReadDb _readDb;

    public GetWarehousesHandler(IRequestTransaction transaction, IWarehouseReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<WarehouseListItem>>> Handle(GetWarehousesQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, WarehouseSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(
            query.Page,
            query.PageSize,
            sort,
            new WarehouseFilter(query.Q, query.LegalEntityId, query.BusinessUnitId, query.IsActive),
            cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

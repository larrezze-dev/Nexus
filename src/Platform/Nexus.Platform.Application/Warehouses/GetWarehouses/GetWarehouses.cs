using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.Warehouses.GetWarehouses;

/// <summary>
/// <c>GET /v1/warehouses</c> (<c>listWarehouses</c>): offset pagination (conventions-api.md §4)
/// with the contract's <c>page</c>, <c>pageSize</c> (max 200), <c>sort</c>
/// (<see cref="WarehouseSortFields.Allowed"/>), free-text <c>q</c> and the
/// <c>legalEntityId</c>/<c>businessUnitId</c>/<c>isActive</c> filters.
/// <para>
/// Scope (ACC-R10): <c>x-scope: none</c> only checks <c>ORG_STRUCTURE:READ</c> in <i>some</i>
/// scope and <c>org</c> has no RLS, so the handler loads every warehouse matching the filters,
/// keeps those whose own <c>WAREHOUSE</c> scope the caller covers (the same check as
/// <c>getWarehouse</c>, via <see cref="OrgListScopeFilter"/>) and only then paginates:
/// <c>total</c> is the count the caller can see.
/// </para>
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
    private const string ReadPermission = "ORG_STRUCTURE:READ";

    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public GetWarehousesHandler(IRequestTransaction transaction, IWarehouseReadDb readDb, IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<PagedResult<WarehouseListItem>>> Handle(GetWarehousesQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, WarehouseSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);

        var candidates = await _readDb.ListAllAsync(
            sort,
            new WarehouseFilter(query.Q, query.LegalEntityId, query.BusinessUnitId, query.IsActive),
            cancellationToken);

        var scope = new OrgListScopeFilter(_scopeChecker, ReadPermission);
        var visible = new List<WarehouseListItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (await scope.CoversAsync(candidate.LegalEntityId, "WAREHOUSE", candidate.Id, cancellationToken))
            {
                visible.Add(candidate);
            }
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(OrgListScopeFilter.Paginate(visible, query.Page, query.PageSize));
    }
}

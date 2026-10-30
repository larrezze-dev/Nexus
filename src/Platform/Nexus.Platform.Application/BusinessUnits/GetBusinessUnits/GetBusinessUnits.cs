using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.BusinessUnits.GetBusinessUnits;

/// <summary>
/// <c>GET /v1/business-units</c> (<c>listBusinessUnits</c>): offset pagination
/// (conventions-api.md §4) with the contract's <c>page</c>, <c>pageSize</c> (max 200) and
/// <c>sort</c> (<see cref="BusinessUnitSortFields.Allowed"/>).
/// </summary>
public sealed record GetBusinessUnitsQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<BusinessUnitListItem>>;

internal sealed class GetBusinessUnitsValidator : AbstractValidator<GetBusinessUnitsQuery>
{
    public GetBusinessUnitsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, BusinessUnitSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetBusinessUnitsHandler : IQueryHandler<GetBusinessUnitsQuery, PagedResult<BusinessUnitListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitReadDb _readDb;

    public GetBusinessUnitsHandler(IRequestTransaction transaction, IBusinessUnitReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<BusinessUnitListItem>>> Handle(GetBusinessUnitsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, BusinessUnitSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

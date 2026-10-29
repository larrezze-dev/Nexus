using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.LegalEntities.GetLegalEntities;

/// <summary>
/// <c>GET /v1/legal-entities</c> (<c>listLegalEntities</c>): offset pagination
/// (conventions-api.md §4) with the contract's <c>page</c>, <c>pageSize</c> (max 200) and
/// <c>sort</c> (<see cref="LegalEntitySortFields.Allowed"/>).
/// </summary>
public sealed record GetLegalEntitiesQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<LegalEntityListItem>>;

internal sealed class GetLegalEntitiesValidator : AbstractValidator<GetLegalEntitiesQuery>
{
    public GetLegalEntitiesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, LegalEntitySortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetLegalEntitiesHandler : IQueryHandler<GetLegalEntitiesQuery, PagedResult<LegalEntityListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityReadDb _readDb;

    public GetLegalEntitiesHandler(IRequestTransaction transaction, ILegalEntityReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<LegalEntityListItem>>> Handle(GetLegalEntitiesQuery query, CancellationToken cancellationToken)
    {
        // Already validated by GetLegalEntitiesValidator; parsing again only to obtain the fields.
        SortSpec.TryParse(query.Sort, LegalEntitySortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

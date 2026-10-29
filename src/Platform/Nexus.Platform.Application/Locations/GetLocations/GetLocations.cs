using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Locations.GetLocations;

/// <summary>
/// <c>GET /v1/locations</c> (<c>listLocations</c>): offset pagination (conventions-api.md §4)
/// with the contract's <c>page</c>, <c>pageSize</c> (max 200) and <c>sort</c>
/// (<see cref="LocationSortFields.Allowed"/>).
/// </summary>
public sealed record GetLocationsQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<LocationListItem>>;

internal sealed class GetLocationsValidator : AbstractValidator<GetLocationsQuery>
{
    public GetLocationsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, LocationSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetLocationsHandler : IQueryHandler<GetLocationsQuery, PagedResult<LocationListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILocationReadDb _readDb;

    public GetLocationsHandler(IRequestTransaction transaction, ILocationReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<LocationListItem>>> Handle(GetLocationsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, LocationSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

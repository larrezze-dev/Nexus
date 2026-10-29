using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Groups.GetGroups;

/// <summary>
/// <c>GET /v1/groups</c> (<c>listGroups</c>): offset pagination (conventions-api.md §4) with the
/// contract's <c>page</c>, <c>pageSize</c> (max 200) and <c>sort</c>
/// (<see cref="GroupSortFields.Allowed"/>).
/// </summary>
public sealed record GetGroupsQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<GroupListItem>>;

internal sealed class GetGroupsValidator : AbstractValidator<GetGroupsQuery>
{
    public GetGroupsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, GroupSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetGroupsHandler : IQueryHandler<GetGroupsQuery, PagedResult<GroupListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IGroupReadDb _readDb;

    public GetGroupsHandler(IRequestTransaction transaction, IGroupReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<GroupListItem>>> Handle(GetGroupsQuery query, CancellationToken cancellationToken)
    {
        // Already validated by GetGroupsValidator; parsing again only to obtain the fields.
        SortSpec.TryParse(query.Sort, GroupSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

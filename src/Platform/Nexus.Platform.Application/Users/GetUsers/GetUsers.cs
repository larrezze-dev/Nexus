using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Users.GetUsers;

/// <summary>
/// <c>GET /v1/users</c> (<c>listUsers</c>): offset pagination (conventions-api.md §4) with the
/// contract's <c>page</c>, <c>pageSize</c> (max 200) and <c>sort</c>
/// (<see cref="UserSortFields.Allowed"/>).
/// </summary>
public sealed record GetUsersQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<UserListItem>>;

internal sealed class GetUsersValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, UserSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetUsersHandler : IQueryHandler<GetUsersQuery, PagedResult<UserListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IUserReadDb _readDb;

    public GetUsersHandler(IRequestTransaction transaction, IUserReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<UserListItem>>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        // Already validated by GetUsersValidator; parsing again only to obtain the fields.
        SortSpec.TryParse(query.Sort, UserSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

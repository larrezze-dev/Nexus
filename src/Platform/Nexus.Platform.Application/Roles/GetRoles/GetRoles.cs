using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Roles.GetRoles;

/// <summary>
/// <c>GET /v1/roles</c> (<c>listRoles</c>): offset pagination (conventions-api.md §4) with the
/// contract's <c>page</c>, <c>pageSize</c> (max 200), <c>sort</c>
/// (<see cref="RoleSortFields.Allowed"/>), free-text <c>q</c> and the <c>isActive</c> filter.
/// </summary>
public sealed record GetRolesQuery(int Page, int PageSize, string? Sort, string? Q, bool? IsActive) : IQuery<PagedResult<RoleListItem>>;

internal sealed class GetRolesValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, RoleSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetRolesHandler : IQueryHandler<GetRolesQuery, PagedResult<RoleListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleReadDb _readDb;

    public GetRolesHandler(IRequestTransaction transaction, IRoleReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<RoleListItem>>> Handle(GetRolesQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, RoleSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, new RoleFilter(query.Q, query.IsActive), cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

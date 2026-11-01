using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.RoleAssignments.GetRoleAssignments;

/// <summary>
/// <c>GET /v1/role-assignments</c> (<c>listRoleAssignments</c>): offset pagination
/// (conventions-api.md §4) with the contract's <c>page</c>, <c>pageSize</c> (max 200),
/// <c>sort</c> (<see cref="RoleAssignmentSortFields.Allowed"/>) and the
/// <c>userId</c>/<c>roleId</c>/<c>scopeType</c>/<c>activeOn</c> filters. <c>activeOn</c> defaults
/// to "now" (<see cref="TimeProvider"/>) when the caller omits it: the endpoint always answers
/// "assignments active at this instant".
/// </summary>
public sealed record GetRoleAssignmentsQuery(
    int Page,
    int PageSize,
    string? Sort,
    Guid? UserId,
    Guid? RoleId,
    string? ScopeType,
    DateTimeOffset? ActiveOn) : IQuery<PagedResult<RoleAssignmentListItem>>;

internal sealed class GetRoleAssignmentsValidator : AbstractValidator<GetRoleAssignmentsQuery>
{
    public GetRoleAssignmentsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, RoleAssignmentSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetRoleAssignmentsHandler : IQueryHandler<GetRoleAssignmentsQuery, PagedResult<RoleAssignmentListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleAssignmentReadDb _readDb;
    private readonly TimeProvider _timeProvider;

    public GetRoleAssignmentsHandler(IRequestTransaction transaction, IRoleAssignmentReadDb readDb, TimeProvider timeProvider)
    {
        _transaction = transaction;
        _readDb = readDb;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PagedResult<RoleAssignmentListItem>>> Handle(
        GetRoleAssignmentsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, RoleAssignmentSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(
            query.Page,
            query.PageSize,
            sort,
            new RoleAssignmentFilter(query.UserId, query.RoleId, query.ScopeType, query.ActiveOn ?? _timeProvider.GetUtcNow()),
            cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

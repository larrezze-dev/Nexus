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
/// <para>
/// Scope: the contract declares <c>x-scope: none</c>, so the permission filter only checks that
/// the caller holds <c>ROLE_ASSIGNMENT:READ</c> <i>somewhere</i> (ACC-R10's coarse gate for
/// listings). ACC-R10 then relies on RLS to narrow a listing's rows, but <c>access</c>
/// deliberately has no RLS (it is the security schema itself). So this handler narrows the rows
/// itself, with the same mechanism <c>revokeRoleAssignment</c> uses: it loads every assignment
/// matching the filters (unpaginated), keeps only those whose own scope the caller's
/// <c>ROLE_ASSIGNMENT:READ</c> <b>covers</b> (<see cref="IRoleAssignmentScopeChecker"/>,
/// GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY ⊃ BUSINESS_UNIT ⊃ WAREHOUSE), and only then paginates, so
/// <c>total</c> is the count the caller can see. A GLOBAL caller still sees every row.
/// <c>getUserEffectiveScope</c>'s broad visibility stays intentional (contract).
/// </para>
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
    private const string ReadPermission = "ROLE_ASSIGNMENT:READ";

    private readonly IRequestTransaction _transaction;
    private readonly IRoleAssignmentReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;
    private readonly TimeProvider _timeProvider;

    public GetRoleAssignmentsHandler(
        IRequestTransaction transaction,
        IRoleAssignmentReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker,
        TimeProvider timeProvider)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PagedResult<RoleAssignmentListItem>>> Handle(
        GetRoleAssignmentsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, RoleAssignmentSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);

        var candidates = await _readDb.ListAllAsync(
            sort,
            new RoleAssignmentFilter(query.UserId, query.RoleId, query.ScopeType, query.ActiveOn ?? _timeProvider.GetUtcNow()),
            cancellationToken);

        // One has_permission call per distinct (scopeType, scopeEntityId): many assignments share
        // a scope (e.g. every grant in one legal entity), and the answer only depends on it.
        var coverage = new Dictionary<(string ScopeType, Guid? ScopeEntityId), bool>();
        var visible = new List<RoleAssignmentListItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var key = (candidate.ScopeType, candidate.ScopeEntityId);
            if (!coverage.TryGetValue(key, out var covered))
            {
                covered = await _scopeChecker.HasPermissionCoveringAsync(
                    ReadPermission, candidate.ScopeType, candidate.ScopeEntityId, cancellationToken);
                coverage[key] = covered;
            }

            if (covered)
            {
                visible.Add(candidate);
            }
        }

        await _transaction.CommitAsync(cancellationToken);

        // Paginate only after filtering: total is what this caller can see, never the unfiltered count.
        var items = visible
            .Skip((int)Math.Min((long)(query.Page - 1) * query.PageSize, int.MaxValue))
            .Take(query.PageSize)
            .ToList();

        return Result.Success(new PagedResult<RoleAssignmentListItem>(items, query.Page, query.PageSize, visible.Count));
    }
}

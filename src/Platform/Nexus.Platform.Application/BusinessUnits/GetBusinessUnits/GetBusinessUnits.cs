using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.BusinessUnits.GetBusinessUnits;

/// <summary>
/// <c>GET /v1/business-units</c> (<c>listBusinessUnits</c>): offset pagination
/// (conventions-api.md §4) with the contract's <c>page</c>, <c>pageSize</c> (max 200) and
/// <c>sort</c> (<see cref="BusinessUnitSortFields.Allowed"/>).
/// <para>
/// Scope (ACC-R10): <c>x-scope: none</c> only checks <c>ORG_STRUCTURE:READ</c> in <i>some</i>
/// scope and <c>org</c> has no RLS, so the handler loads every business unit, keeps those whose
/// own <c>BUSINESS_UNIT</c> scope the caller covers (the same check as
/// <c>getBusinessUnit</c>, via <see cref="OrgListScopeFilter"/>) and only then paginates:
/// <c>total</c> is the count the caller can see.
/// </para>
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
    private const string ReadPermission = "ORG_STRUCTURE:READ";

    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public GetBusinessUnitsHandler(IRequestTransaction transaction, IBusinessUnitReadDb readDb, IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<PagedResult<BusinessUnitListItem>>> Handle(GetBusinessUnitsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, BusinessUnitSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);

        var candidates = await _readDb.ListAllAsync(sort, cancellationToken);

        var scope = new OrgListScopeFilter(_scopeChecker, ReadPermission);
        var visible = new List<BusinessUnitListItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (await scope.CoversAsync(candidate.LegalEntityId, "BUSINESS_UNIT", candidate.Id, cancellationToken))
            {
                visible.Add(candidate);
            }
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(OrgListScopeFilter.Paginate(visible, query.Page, query.PageSize));
    }
}

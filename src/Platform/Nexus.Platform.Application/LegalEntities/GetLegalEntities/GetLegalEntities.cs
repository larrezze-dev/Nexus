using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.LegalEntities.GetLegalEntities;

/// <summary>
/// <c>GET /v1/legal-entities</c> (<c>listLegalEntities</c>): offset pagination
/// (conventions-api.md §4) with the contract's <c>page</c>, <c>pageSize</c> (max 200) and
/// <c>sort</c> (<see cref="LegalEntitySortFields.Allowed"/>).
/// <para>
/// Scope (ACC-R10): <c>x-scope: none</c> only checks <c>ORG_STRUCTURE:READ</c> in <i>some</i>
/// scope and <c>org</c> has no RLS, so the handler loads every legal entity, keeps those whose
/// <c>LEGAL_ENTITY</c> scope the caller covers (the same check as <c>getLegalEntity</c>, via
/// <see cref="OrgListScopeFilter"/>) and only then paginates: <c>total</c> is the count the
/// caller can see.
/// </para>
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
    private const string ReadPermission = "ORG_STRUCTURE:READ";

    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public GetLegalEntitiesHandler(IRequestTransaction transaction, ILegalEntityReadDb readDb, IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<PagedResult<LegalEntityListItem>>> Handle(GetLegalEntitiesQuery query, CancellationToken cancellationToken)
    {
        // Already validated by GetLegalEntitiesValidator; parsing again only to obtain the fields.
        SortSpec.TryParse(query.Sort, LegalEntitySortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);

        var candidates = await _readDb.ListAllAsync(sort, cancellationToken);

        var scope = new OrgListScopeFilter(_scopeChecker, ReadPermission);
        var visible = new List<LegalEntityListItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            if (await scope.CoversAsync(candidate.Id, "LEGAL_ENTITY", candidate.Id, cancellationToken))
            {
                visible.Add(candidate);
            }
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(OrgListScopeFilter.Paginate(visible, query.Page, query.PageSize));
    }
}

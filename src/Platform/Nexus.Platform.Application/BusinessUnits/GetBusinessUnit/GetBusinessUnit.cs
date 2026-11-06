using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.BusinessUnits.GetBusinessUnit;

/// <summary>
/// <c>GET /v1/business-units/{id}</c> (<c>getBusinessUnit</c>): detail plus version for the <c>ETag</c>.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers the business unit's own scope
/// (<c>BUSINESS_UNIT</c>, which passes its <c>legal_entity_id</c> to
/// <c>access.has_permission</c> too: LE/GROUP/GLOBAL grants cover it, and so does a grant on the
/// business unit itself) and answers 403 otherwise.
/// </para>
/// </summary>
public sealed record GetBusinessUnitQuery(Guid Id) : IQuery<BusinessUnitDetails>;

internal sealed class GetBusinessUnitHandler : IQueryHandler<GetBusinessUnitQuery, BusinessUnitDetails>
{
    private const string ReadPermission = "ORG_STRUCTURE:READ";

    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitReadDb _readDb;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public GetBusinessUnitHandler(IRequestTransaction transaction, IBusinessUnitReadDb readDb, IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<BusinessUnitDetails>> Handle(GetBusinessUnitQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var businessUnit = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        if (businessUnit is null)
        {
            return Result.Failure<BusinessUnitDetails>(BusinessUnitErrors.NotFound(query.Id));
        }

        if (!await _scopeChecker.HasPermissionCoveringAsync(ReadPermission, "BUSINESS_UNIT", businessUnit.Id, cancellationToken))
        {
            return Result.Failure<BusinessUnitDetails>(BusinessUnitErrors.ScopeNotCovered);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(businessUnit);
    }
}

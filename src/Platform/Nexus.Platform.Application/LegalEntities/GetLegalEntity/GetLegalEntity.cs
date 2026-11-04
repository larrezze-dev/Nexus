using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.LegalEntities.GetLegalEntity;

/// <summary>
/// <c>GET /v1/legal-entities/{id}</c> (<c>getLegalEntity</c>): detail plus version for the <c>ETag</c>.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers this legal entity (the row's own id
/// is its <c>legal_entity_id</c>: LE, GROUP and GLOBAL grants cover it) and answers 403 otherwise.
/// </para>
/// </summary>
public sealed record GetLegalEntityQuery(Guid Id) : IQuery<LegalEntityDetails>;

internal sealed class GetLegalEntityHandler : IQueryHandler<GetLegalEntityQuery, LegalEntityDetails>
{
    private const string ReadPermission = "ORG_STRUCTURE:READ";

    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityReadDb _readDb;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public GetLegalEntityHandler(IRequestTransaction transaction, ILegalEntityReadDb readDb, IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<LegalEntityDetails>> Handle(GetLegalEntityQuery query, CancellationToken cancellationToken)
    {
        // BE-R1: reads go through the unit of work too (RLS session), even on a GLOBAL schema.
        await _transaction.BeginAsync(cancellationToken);

        var legalEntity = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        if (legalEntity is null)
        {
            return Result.Failure<LegalEntityDetails>(LegalEntityErrors.NotFound(query.Id));
        }

        if (!await _scopeChecker.HasPermissionCoveringAsync(ReadPermission, "LEGAL_ENTITY", legalEntity.Id, cancellationToken))
        {
            return Result.Failure<LegalEntityDetails>(LegalEntityErrors.ScopeNotCovered);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(legalEntity);
    }
}

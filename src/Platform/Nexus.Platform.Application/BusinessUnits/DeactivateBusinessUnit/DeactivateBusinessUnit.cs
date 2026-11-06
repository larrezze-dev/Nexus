using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.BusinessUnits.DeactivateBusinessUnit;

/// <summary>
/// <c>POST /v1/business-units/{id}/deactivate</c> (<c>deactivateBusinessUnit</c>), guarded by
/// <c>If-Match</c>. Idempotent: an already inactive business unit is returned unchanged,
/// without a new event.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers the business unit's own scope
/// (<c>BUSINESS_UNIT</c>, which passes its <c>legal_entity_id</c> to
/// <c>access.has_permission</c> too: LE/GROUP/GLOBAL grants cover it, and so does a grant on the
/// business unit itself) and answers 403 otherwise.
/// </para>
/// </summary>
public sealed record DeactivateBusinessUnitCommand(Guid Id, uint ExpectedVersion) : ICommand<BusinessUnitDetails>;

internal sealed class DeactivateBusinessUnitHandler : ICommandHandler<DeactivateBusinessUnitCommand, BusinessUnitDetails>
{
    private const string UpdatePermission = "ORG_STRUCTURE:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitRepository _businessUnits;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public DeactivateBusinessUnitHandler(
        IRequestTransaction transaction,
        IBusinessUnitRepository businessUnits,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _businessUnits = businessUnits;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<BusinessUnitDetails>> Handle(DeactivateBusinessUnitCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await VersionedLoader.LoadAsync(
            _businessUnits.GetByIdAsync,
            _businessUnits.GetMetadata,
            command.Id,
            command.ExpectedVersion,
            BusinessUnitErrors.NotFound(command.Id),
            BusinessUnitErrors.StaleVersion,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<BusinessUnitDetails>(loaded.Error!);
        }

        var businessUnit = loaded.Value;

        // Before any change (and before the idempotent no-op on deactivate).
        if (!await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "BUSINESS_UNIT", businessUnit.Id, cancellationToken))
        {
            return Result.Failure<BusinessUnitDetails>(BusinessUnitErrors.ScopeNotCovered);
        }

        businessUnit.Deactivate();

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(BusinessUnitDetails.From(businessUnit, _businessUnits.GetMetadata(businessUnit)));
    }
}

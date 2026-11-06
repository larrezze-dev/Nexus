using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.BusinessUnits;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.Warehouses.UpdateWarehouse;

/// <summary>
/// <c>PUT /v1/warehouses/{id}</c> (<c>updateWarehouse</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>). <c>legalEntityId</c> never changes (not part
/// of <c>UpdateWarehouseRequest</c>); ORG-R3 is revalidated against the warehouse's own
/// (unchanged) <c>LegalEntityId</c> whenever <see cref="BusinessUnitId"/> is set, the same
/// handler-side check as <c>CreateWarehouse</c> and for the same reason.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers the warehouse's own scope
/// (<c>WAREHOUSE</c>, which passes its <c>legal_entity_id</c> and <c>business_unit_id</c> to
/// <c>access.has_permission</c> too: LE/GROUP/GLOBAL grants cover it, and so do narrower grants
/// on the warehouse itself or its business unit) and answers 403 otherwise.
/// </para>
/// <para>
/// Moving the warehouse to a <b>different</b> business unit also requires the caller's
/// <c>ORG_STRUCTURE:UPDATE</c> to cover that target business unit: otherwise a grant narrower
/// than the legal entity (on this warehouse or its current business unit) could re-parent it
/// under a business unit the caller has no authority over.
/// </para>
/// </summary>
public sealed record UpdateWarehouseCommand(
    Guid Id,
    uint ExpectedVersion,
    Guid LocationId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n) : ICommand<WarehouseDetails>;

internal sealed class UpdateWarehouseValidator : AbstractValidator<UpdateWarehouseCommand>
{
    public UpdateWarehouseValidator()
    {
        RuleFor(c => c.LocationId).Must(id => id != Guid.Empty).WithMessage("'locationId' must not be an empty UUID.");
        RuleFor(c => c.BusinessUnitId).Must(id => id != Guid.Empty).WithMessage("'businessUnitId' must not be an empty UUID.");
        RuleFor(c => c.Code).MustBeNonBlank();
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
    }
}

internal sealed class UpdateWarehouseHandler : ICommandHandler<UpdateWarehouseCommand, WarehouseDetails>
{
    private const string UpdatePermission = "ORG_STRUCTURE:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseRepository _warehouses;
    private readonly IBusinessUnitReadDb _businessUnits;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public UpdateWarehouseHandler(
        IRequestTransaction transaction,
        IWarehouseRepository warehouses,
        IBusinessUnitReadDb businessUnits,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _warehouses = warehouses;
        _businessUnits = businessUnits;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<WarehouseDetails>> Handle(UpdateWarehouseCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var loaded = await VersionedLoader.LoadAsync(
            _warehouses.GetByIdAsync,
            _warehouses.GetMetadata,
            command.Id,
            command.ExpectedVersion,
            WarehouseErrors.NotFound(command.Id),
            WarehouseErrors.StaleVersion,
            cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<WarehouseDetails>(loaded.Error!);
        }

        var warehouse = loaded.Value;

        // Before ORG-R3 and before any change: the row's own scope first, then the target
        // business unit if it changes (an unknown id is only covered by GLOBAL; the FK answers then).
        if (!await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "WAREHOUSE", warehouse.Id, cancellationToken))
        {
            return Result.Failure<WarehouseDetails>(WarehouseErrors.ScopeNotCovered);
        }

        if (command.BusinessUnitId is { } targetBusinessUnitId
            && targetBusinessUnitId != warehouse.BusinessUnitId
            && !await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "BUSINESS_UNIT", targetBusinessUnitId, cancellationToken))
        {
            return Result.Failure<WarehouseDetails>(WarehouseErrors.ScopeNotCovered);
        }

        if (command.BusinessUnitId is { } businessUnitId)
        {
            var businessUnitLegalEntityId = await _businessUnits.FindLegalEntityIdAsync(businessUnitId, cancellationToken);
            if (businessUnitLegalEntityId is { } ownerLegalEntityId && ownerLegalEntityId != warehouse.LegalEntityId)
            {
                return Result.Failure<WarehouseDetails>(WarehouseErrors.LegalEntityMismatch);
            }
        }

        warehouse.Update(command.LocationId, command.BusinessUnitId, command.Code, command.CaptionI18n);

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(WarehouseDetails.From(warehouse, _warehouses.GetMetadata(warehouse)));
    }
}

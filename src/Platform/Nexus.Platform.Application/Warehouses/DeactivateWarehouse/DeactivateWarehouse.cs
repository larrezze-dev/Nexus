using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.Warehouses.DeactivateWarehouse;

/// <summary>
/// <c>POST /v1/warehouses/{id}/deactivate</c> (<c>deactivateWarehouse</c>), guarded by
/// <c>If-Match</c>. Idempotent: an already inactive warehouse is returned unchanged, without a
/// new event.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers the warehouse's own scope
/// (<c>WAREHOUSE</c>, which passes its <c>legal_entity_id</c> and <c>business_unit_id</c> to
/// <c>access.has_permission</c> too: LE/GROUP/GLOBAL grants cover it, and so do narrower grants
/// on the warehouse itself or its business unit) and answers 403 otherwise.
/// </para>
/// </summary>
public sealed record DeactivateWarehouseCommand(Guid Id, uint ExpectedVersion) : ICommand<WarehouseDetails>;

internal sealed class DeactivateWarehouseHandler : ICommandHandler<DeactivateWarehouseCommand, WarehouseDetails>
{
    private const string UpdatePermission = "ORG_STRUCTURE:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseRepository _warehouses;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public DeactivateWarehouseHandler(
        IRequestTransaction transaction,
        IWarehouseRepository warehouses,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _warehouses = warehouses;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<WarehouseDetails>> Handle(DeactivateWarehouseCommand command, CancellationToken cancellationToken)
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

        // Before the idempotent no-op: a caller whose scope does not cover the row learns nothing more.
        if (!await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "WAREHOUSE", warehouse.Id, cancellationToken))
        {
            return Result.Failure<WarehouseDetails>(WarehouseErrors.ScopeNotCovered);
        }

        warehouse.Deactivate();

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(WarehouseDetails.From(warehouse, _warehouses.GetMetadata(warehouse)));
    }
}

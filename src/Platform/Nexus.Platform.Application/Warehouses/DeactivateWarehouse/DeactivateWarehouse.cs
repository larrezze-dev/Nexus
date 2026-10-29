using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.Warehouses.DeactivateWarehouse;

/// <summary>
/// <c>POST /v1/warehouses/{id}/deactivate</c> (<c>deactivateWarehouse</c>), guarded by
/// <c>If-Match</c>. Idempotent: an already inactive warehouse is returned unchanged, without a
/// new event.
/// </summary>
public sealed record DeactivateWarehouseCommand(Guid Id, uint ExpectedVersion) : ICommand<WarehouseDetails>;

internal sealed class DeactivateWarehouseHandler : ICommandHandler<DeactivateWarehouseCommand, WarehouseDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseRepository _warehouses;

    public DeactivateWarehouseHandler(IRequestTransaction transaction, IWarehouseRepository warehouses)
    {
        _transaction = transaction;
        _warehouses = warehouses;
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
        warehouse.Deactivate();

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(WarehouseDetails.From(warehouse, _warehouses.GetMetadata(warehouse)));
    }
}

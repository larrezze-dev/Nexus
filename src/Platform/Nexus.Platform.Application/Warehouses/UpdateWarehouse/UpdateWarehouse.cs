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
    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseRepository _warehouses;
    private readonly IBusinessUnitReadDb _businessUnits;

    public UpdateWarehouseHandler(
        IRequestTransaction transaction,
        IWarehouseRepository warehouses,
        IBusinessUnitReadDb businessUnits)
    {
        _transaction = transaction;
        _warehouses = warehouses;
        _businessUnits = businessUnits;
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

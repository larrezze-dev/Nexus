using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Application.BusinessUnits.DeactivateBusinessUnit;

/// <summary>
/// <c>POST /v1/business-units/{id}/deactivate</c> (<c>deactivateBusinessUnit</c>), guarded by
/// <c>If-Match</c>. Idempotent: an already inactive business unit is returned unchanged,
/// without a new event.
/// </summary>
public sealed record DeactivateBusinessUnitCommand(Guid Id, uint ExpectedVersion) : ICommand<BusinessUnitDetails>;

internal sealed class DeactivateBusinessUnitHandler : ICommandHandler<DeactivateBusinessUnitCommand, BusinessUnitDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitRepository _businessUnits;

    public DeactivateBusinessUnitHandler(IRequestTransaction transaction, IBusinessUnitRepository businessUnits)
    {
        _transaction = transaction;
        _businessUnits = businessUnits;
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
        businessUnit.Deactivate();

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(BusinessUnitDetails.From(businessUnit, _businessUnits.GetMetadata(businessUnit)));
    }
}

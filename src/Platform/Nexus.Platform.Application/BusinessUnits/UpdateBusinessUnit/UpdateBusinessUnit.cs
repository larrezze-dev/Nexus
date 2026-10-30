using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.BusinessUnits.UpdateBusinessUnit;

/// <summary>
/// <c>PUT /v1/business-units/{id}</c> (<c>updateBusinessUnit</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>, the row's <c>xmin</c>). <c>legalEntityId</c>
/// never changes (not part of <c>UpdateBusinessUnitRequest</c>).
/// </summary>
public sealed record UpdateBusinessUnitCommand(
    Guid Id,
    uint ExpectedVersion,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? CostCenterCode) : ICommand<BusinessUnitDetails>;

internal sealed class UpdateBusinessUnitValidator : AbstractValidator<UpdateBusinessUnitCommand>
{
    public UpdateBusinessUnitValidator()
    {
        RuleFor(c => c.Code).MustBeNonBlank();
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
    }
}

internal sealed class UpdateBusinessUnitHandler : ICommandHandler<UpdateBusinessUnitCommand, BusinessUnitDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitRepository _businessUnits;

    public UpdateBusinessUnitHandler(IRequestTransaction transaction, IBusinessUnitRepository businessUnits)
    {
        _transaction = transaction;
        _businessUnits = businessUnits;
    }

    public async Task<Result<BusinessUnitDetails>> Handle(UpdateBusinessUnitCommand command, CancellationToken cancellationToken)
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
        businessUnit.Update(command.Code, command.CaptionI18n, command.CostCenterCode);

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(BusinessUnitDetails.From(businessUnit, _businessUnits.GetMetadata(businessUnit)));
    }
}

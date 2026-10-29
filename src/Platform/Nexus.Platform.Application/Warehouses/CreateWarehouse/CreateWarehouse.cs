using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.BusinessUnits;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.Warehouses;

namespace Nexus.Platform.Application.Warehouses.CreateWarehouse;

/// <summary>
/// <c>POST /v1/warehouses</c> (<c>createWarehouse</c>): idempotent create (client-supplied
/// UUIDv7 <see cref="Id"/>).
/// <para>
/// Rule ORG-R3 ("if <c>businessUnitId</c> is set, it must belong to the same
/// <c>legalEntityId</c>") is checked <b>here</b>, before calling <see cref="Warehouse.Create"/>,
/// not inside the aggregate: it needs the referenced <c>BusinessUnit</c>'s own
/// <c>legal_entity_id</c>, a fact that belongs to another aggregate and can only be read from
/// the database (<see cref="IBusinessUnitReadDb.FindLegalEntityIdAsync"/>) - the pure
/// <c>Warehouse</c> aggregate has no persistence access (conventions-backend.md §3). When the
/// referenced business unit does not exist at all, the mismatch check is skipped on purpose and
/// the insert is left to fail its own foreign key, translated to
/// <see cref="WarehouseErrors.BusinessUnitNotFound"/> - otherwise a typo'd id would surface as
/// the wrong error (ORG-R3 instead of "not found").
/// </para>
/// </summary>
public sealed record CreateWarehouseCommand(
    Guid? Id,
    Guid LocationId,
    Guid LegalEntityId,
    Guid? BusinessUnitId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n) : ICommand<CreateWarehouseResult>;

/// <summary>The warehouse and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateWarehouseResult(WarehouseDetails Warehouse, bool Created);

internal sealed class CreateWarehouseValidator : AbstractValidator<CreateWarehouseCommand>
{
    public CreateWarehouseValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.LocationId).Must(id => id != Guid.Empty).WithMessage("'locationId' must not be an empty UUID.");
        RuleFor(c => c.LegalEntityId).Must(id => id != Guid.Empty).WithMessage("'legalEntityId' must not be an empty UUID.");
        RuleFor(c => c.BusinessUnitId).Must(id => id != Guid.Empty).WithMessage("'businessUnitId' must not be an empty UUID.");
        RuleFor(c => c.Code).MustBeNonBlank();
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
    }
}

internal sealed class CreateWarehouseHandler : ICommandHandler<CreateWarehouseCommand, CreateWarehouseResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseRepository _warehouses;
    private readonly IBusinessUnitReadDb _businessUnits;
    private readonly IIdGenerator _ids;

    public CreateWarehouseHandler(
        IRequestTransaction transaction,
        IWarehouseRepository warehouses,
        IBusinessUnitReadDb businessUnits,
        IIdGenerator ids)
    {
        _transaction = transaction;
        _warehouses = warehouses;
        _businessUnits = businessUnits;
        _ids = ids;
    }

    public async Task<Result<CreateWarehouseResult>> Handle(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _warehouses.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        if (command.BusinessUnitId is { } businessUnitId)
        {
            var businessUnitLegalEntityId = await _businessUnits.FindLegalEntityIdAsync(businessUnitId, cancellationToken);
            if (businessUnitLegalEntityId is { } ownerLegalEntityId && ownerLegalEntityId != command.LegalEntityId)
            {
                return Result.Failure<CreateWarehouseResult>(WarehouseErrors.LegalEntityMismatch);
            }
        }

        var warehouseResult = Warehouse.Create(
            command.Id ?? _ids.NewId(),
            command.LocationId,
            command.LegalEntityId,
            command.BusinessUnitId,
            command.Code,
            command.CaptionI18n);
        if (warehouseResult.IsFailure)
        {
            return Result.Failure<CreateWarehouseResult>(warehouseResult.Error!);
        }

        var warehouse = warehouseResult.Value;
        _warehouses.Add(warehouse);

        // locationId/legalEntityId/businessUnitId existence and the code's global uniqueness
        // are enforced by the database at this point; their violations are translated by the
        // web layer (WarehouseErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateWarehouseResult(
            WarehouseDetails.From(warehouse, _warehouses.GetMetadata(warehouse)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateWarehouseResult>> ReplayAsync(
        CreateWarehouseCommand command, Warehouse existing, CancellationToken cancellationToken)
    {
        var identical =
            existing.LocationId == command.LocationId
            && existing.LegalEntityId == command.LegalEntityId
            && existing.BusinessUnitId == command.BusinessUnitId
            && string.Equals(existing.Code, command.Code, StringComparison.Ordinal)
            && CaptionEquals(existing.CaptionI18n, command.CaptionI18n);

        if (!identical)
        {
            return Result.Failure<CreateWarehouseResult>(WarehouseErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateWarehouseResult(
            WarehouseDetails.From(existing, _warehouses.GetMetadata(existing)), Created: false));
    }

    private static bool CaptionEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
}

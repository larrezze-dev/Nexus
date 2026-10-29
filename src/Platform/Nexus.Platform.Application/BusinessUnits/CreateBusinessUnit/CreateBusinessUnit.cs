using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.BusinessUnits.CreateBusinessUnit;

/// <summary>
/// <c>POST /v1/business-units</c> (<c>createBusinessUnit</c>): creates the business unit's
/// internal <c>ORGANIZATION</c> <see cref="Party"/> and the <see cref="BusinessUnit"/> in one
/// transaction (two aggregates in one command, the same documented exception as <c>CreateUser</c>,
/// conventions-backend.md §6).
/// <para>
/// Idempotent (conventions-api.md §5): with a client-supplied <see cref="Id"/> that already
/// exists, identical content → the existing business unit (<see cref="CreateBusinessUnitResult.Created"/>
/// = <see langword="false"/>, 200); different content → 409 <c>CONFLICT</c>.
/// </para>
/// </summary>
public sealed record CreateBusinessUnitCommand(
    Guid? Id,
    string LegalName,
    string? DisplayName,
    Guid LegalEntityId,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? CostCenterCode) : ICommand<CreateBusinessUnitResult>;

/// <summary>The business unit and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateBusinessUnitResult(BusinessUnitDetails BusinessUnit, bool Created);

internal sealed class CreateBusinessUnitValidator : AbstractValidator<CreateBusinessUnitCommand>
{
    public CreateBusinessUnitValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.LegalName).MustBeNonBlank();
        RuleFor(c => c.LegalEntityId).Must(id => id != Guid.Empty).WithMessage("'legalEntityId' must not be an empty UUID.");
        RuleFor(c => c.Code).MustBeNonBlank();
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
    }
}

internal sealed class CreateBusinessUnitHandler : ICommandHandler<CreateBusinessUnitCommand, CreateBusinessUnitResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitRepository _businessUnits;
    private readonly IPartyRepository _parties;
    private readonly IIdGenerator _ids;

    public CreateBusinessUnitHandler(
        IRequestTransaction transaction,
        IBusinessUnitRepository businessUnits,
        IPartyRepository parties,
        IIdGenerator ids)
    {
        _transaction = transaction;
        _businessUnits = businessUnits;
        _parties = parties;
        _ids = ids;
    }

    public async Task<Result<CreateBusinessUnitResult>> Handle(CreateBusinessUnitCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _businessUnits.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        var partyResult = Party.Create(
            _ids.NewId(),
            PartyType.Organization,
            command.LegalName,
            command.DisplayName,
            taxId: null,
            erpCompanyCode: null,
            isInternal: true); // Rule ORG-R1: a business unit's party is an internal ORGANIZATION.
        if (partyResult.IsFailure)
        {
            return Result.Failure<CreateBusinessUnitResult>(partyResult.Error!);
        }

        var party = partyResult.Value;
        var businessUnitResult = BusinessUnit.Create(
            command.Id ?? _ids.NewId(),
            party,
            command.LegalEntityId,
            command.Code,
            command.CaptionI18n,
            command.CostCenterCode);
        if (businessUnitResult.IsFailure)
        {
            return Result.Failure<CreateBusinessUnitResult>(businessUnitResult.Error!);
        }

        var businessUnit = businessUnitResult.Value;
        _parties.Add(party);
        _businessUnits.Add(businessUnit);

        // The code's uniqueness within the legal entity and the legal entity's existence are
        // enforced by the database at this point; their violations are translated by the web
        // layer (BusinessUnitErrors, AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateBusinessUnitResult(
            BusinessUnitDetails.From(businessUnit, _businessUnits.GetMetadata(businessUnit)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateBusinessUnitResult>> ReplayAsync(
        CreateBusinessUnitCommand command, BusinessUnit existing, CancellationToken cancellationToken)
    {
        var party = await _parties.GetByIdAsync(existing.PartyId, cancellationToken)
            ?? throw new InvalidOperationException($"Business unit '{existing.Id}' references a missing party '{existing.PartyId}'.");

        var identical =
            string.Equals(party.LegalName, command.LegalName, StringComparison.Ordinal)
            && string.Equals(party.DisplayName, command.DisplayName, StringComparison.Ordinal)
            && existing.LegalEntityId == command.LegalEntityId
            && string.Equals(existing.Code, command.Code, StringComparison.Ordinal)
            && CaptionEquals(existing.CaptionI18n, command.CaptionI18n)
            && string.Equals(existing.CostCenterCode, command.CostCenterCode, StringComparison.Ordinal);

        if (!identical)
        {
            return Result.Failure<CreateBusinessUnitResult>(BusinessUnitErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateBusinessUnitResult(
            BusinessUnitDetails.From(existing, _businessUnits.GetMetadata(existing)), Created: false));
    }

    private static bool CaptionEquals(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
}

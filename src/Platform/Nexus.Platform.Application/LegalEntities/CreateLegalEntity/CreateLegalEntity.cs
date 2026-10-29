using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Currencies;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.LegalEntities.CreateLegalEntity;

/// <summary>
/// <c>POST /v1/legal-entities</c> (<c>createLegalEntity</c>): creates the legal entity's
/// internal <c>ORGANIZATION</c> <see cref="Party"/> and the <see cref="LegalEntity"/> in one
/// transaction (ORG-R1, two aggregates in one command, the same documented exception as
/// <c>CreateUser</c>/<c>CreateGroup</c>).
/// <para>
/// Idempotent (conventions-api.md §5): with a client-supplied <see cref="Id"/> that already
/// exists, identical content → the existing legal entity
/// (<see cref="CreateLegalEntityResult.Created"/> = <see langword="false"/>, 200); different
/// content → 409 <c>CONFLICT</c>.
/// </para>
/// </summary>
public sealed record CreateLegalEntityCommand(
    Guid? Id,
    string LegalName,
    string? DisplayName,
    Guid GroupId,
    string Code,
    string CurrencyCode,
    string CountryCode) : ICommand<CreateLegalEntityResult>;

/// <summary>The legal entity and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreateLegalEntityResult(LegalEntityDetails LegalEntity, bool Created);

internal sealed class CreateLegalEntityValidator : AbstractValidator<CreateLegalEntityCommand>
{
    public CreateLegalEntityValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.LegalName).MustBeNonBlank();
        RuleFor(c => c.GroupId).NotEqual(Guid.Empty).WithMessage("'groupId' must not be an empty UUID.");
        RuleFor(c => c.Code).MustBeNonBlank().Matches(@"^[A-Z0-9_\-\.]{1,40}$");
        RuleFor(c => c.CurrencyCode).Matches(@"^[A-Z]{3}$");
        RuleFor(c => c.CountryCode).Matches(@"^[A-Z]{2}$");
    }
}

internal sealed class CreateLegalEntityHandler : ICommandHandler<CreateLegalEntityCommand, CreateLegalEntityResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly IPartyRepository _parties;
    private readonly ICurrencyReadDb _currencies;
    private readonly IIdGenerator _ids;

    public CreateLegalEntityHandler(
        IRequestTransaction transaction,
        ILegalEntityRepository legalEntities,
        IPartyRepository parties,
        ICurrencyReadDb currencies,
        IIdGenerator ids)
    {
        _transaction = transaction;
        _legalEntities = legalEntities;
        _parties = parties;
        _currencies = currencies;
        _ids = ids;
    }

    public async Task<Result<CreateLegalEntityResult>> Handle(CreateLegalEntityCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _legalEntities.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        // Rule ORG-R7: the currency must exist and be active.
        var currencyCheck = await CurrencyRules.EnsureActiveAsync(_currencies, command.CurrencyCode, cancellationToken);
        if (currencyCheck.IsFailure)
        {
            return Result.Failure<CreateLegalEntityResult>(currencyCheck.Error!);
        }

        var partyResult = Party.Create(
            _ids.NewId(),
            PartyType.Organization,
            command.LegalName,
            command.DisplayName,
            taxId: null,
            erpCompanyCode: null,
            isInternal: true); // Rule ORG-R1: a LegalEntity's party is an internal ORGANIZATION.
        if (partyResult.IsFailure)
        {
            return Result.Failure<CreateLegalEntityResult>(partyResult.Error!);
        }

        var party = partyResult.Value;
        var legalEntityResult = LegalEntity.Create(
            command.Id ?? _ids.NewId(),
            party,
            command.GroupId,
            command.Code,
            command.CurrencyCode,
            command.CountryCode);
        if (legalEntityResult.IsFailure)
        {
            return Result.Failure<CreateLegalEntityResult>(legalEntityResult.Error!);
        }

        var legalEntity = legalEntityResult.Value;
        _parties.Add(party);
        _legalEntities.Add(legalEntity);

        // The code's uniqueness and the group's existence are enforced by the database at this
        // point; their violations are translated by the web layer (LegalEntityErrors,
        // AddNexusConstraintViolation).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreateLegalEntityResult(
            LegalEntityDetails.From(legalEntity, _legalEntities.GetMetadata(legalEntity)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreateLegalEntityResult>> ReplayAsync(
        CreateLegalEntityCommand command, LegalEntity existing, CancellationToken cancellationToken)
    {
        var identical =
            existing.GroupId == command.GroupId
            && string.Equals(existing.Code, command.Code, StringComparison.Ordinal)
            && string.Equals(existing.CurrencyCode, command.CurrencyCode, StringComparison.Ordinal)
            && string.Equals(existing.CountryCode, command.CountryCode, StringComparison.Ordinal);

        if (!identical)
        {
            return Result.Failure<CreateLegalEntityResult>(LegalEntityErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreateLegalEntityResult(
            LegalEntityDetails.From(existing, _legalEntities.GetMetadata(existing)), Created: false));
    }
}

using FluentValidation;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.Parties.CreateParty;

/// <summary>
/// <c>POST /v1/parties</c> (<c>createParty</c>). Idempotent (conventions-api.md §5): with a
/// client-supplied <see cref="Id"/> that already exists, identical content → the existing party
/// (<see cref="CreatePartyResult.Created"/> = <see langword="false"/>, 200); different content →
/// 409 <c>CONFLICT</c>.
/// </summary>
public sealed record CreatePartyCommand(
    Guid? Id,
    string PartyType,
    string LegalName,
    string? DisplayName,
    string? TaxId,
    string? ErpCompanyCode,
    bool IsInternal) : ICommand<CreatePartyResult>;

/// <summary>The party and whether this call created it (201) or found it already there (200).</summary>
public sealed record CreatePartyResult(PartyDetails Party, bool Created);

internal sealed class CreatePartyValidator : AbstractValidator<CreatePartyCommand>
{
    public CreatePartyValidator()
    {
        RuleFor(c => c.Id)
            .Must(id => id is null || (id.Value != Guid.Empty && id.Value.Version == 7))
            .WithMessage("'id' must be a UUIDv7 (ADR-0009).");
        RuleFor(c => c.PartyType)
            .NotNull()
            .MustBeOneOf([PartyTypeNames.Organization, PartyTypeNames.Person])
            .WithMessage($"'partyType' must be one of: {PartyTypeNames.Organization}, {PartyTypeNames.Person}.");
        RuleFor(c => c.LegalName).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("'legalName' must not be empty.");
    }
}

internal sealed class CreatePartyHandler : ICommandHandler<CreatePartyCommand, CreatePartyResult>
{
    private readonly IRequestTransaction _transaction;
    private readonly IPartyRepository _parties;
    private readonly IIdGenerator _ids;

    public CreatePartyHandler(IRequestTransaction transaction, IPartyRepository parties, IIdGenerator ids)
    {
        _transaction = transaction;
        _parties = parties;
        _ids = ids;
    }

    public async Task<Result<CreatePartyResult>> Handle(CreatePartyCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (command.Id is { } requestedId && await _parties.GetByIdAsync(requestedId, cancellationToken) is { } existing)
        {
            return await ReplayAsync(command, existing, cancellationToken);
        }

        var partyResult = Party.Create(
            command.Id ?? _ids.NewId(),
            PartyTypeNames.Parse(command.PartyType),
            command.LegalName,
            command.DisplayName,
            command.TaxId,
            command.ErpCompanyCode,
            command.IsInternal);
        if (partyResult.IsFailure)
        {
            return Result.Failure<CreatePartyResult>(partyResult.Error!);
        }

        var party = partyResult.Value;
        _parties.Add(party);

        // A concurrent create with the same client id is enforced by the primary key and
        // translated by the web layer (AddNexusConstraintViolation, PartyErrors.IdAlreadyUsed).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(new CreatePartyResult(PartyDetails.From(party, _parties.GetMetadata(party)), Created: true));
    }

    /// <summary>Same id again: identical content is a replay (200), anything else a conflict (409).</summary>
    private async Task<Result<CreatePartyResult>> ReplayAsync(CreatePartyCommand command, Party existing, CancellationToken cancellationToken)
    {
        var identical =
            string.Equals(existing.PartyType.ToName(), command.PartyType, StringComparison.Ordinal)
            && string.Equals(existing.LegalName, command.LegalName, StringComparison.Ordinal)
            && string.Equals(existing.DisplayName, command.DisplayName, StringComparison.Ordinal)
            && string.Equals(existing.TaxId, command.TaxId, StringComparison.Ordinal)
            && string.Equals(existing.ErpCompanyCode, command.ErpCompanyCode, StringComparison.Ordinal)
            && existing.IsInternal == command.IsInternal;

        if (!identical)
        {
            return Result.Failure<CreatePartyResult>(PartyErrors.IdAlreadyUsed);
        }

        await _transaction.CommitAsync(cancellationToken);
        return Result.Success(new CreatePartyResult(PartyDetails.From(existing, _parties.GetMetadata(existing)), Created: false));
    }
}

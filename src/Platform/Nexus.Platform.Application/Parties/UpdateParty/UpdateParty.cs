using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Parties.UpdateParty;

/// <summary>
/// <c>PUT /v1/parties/{id}</c> (<c>updateParty</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>). <c>partyType</c> and <c>isInternal</c> are
/// not here: ORG-R2 keeps them fixed after creation and <c>UpdatePartyRequest</c> does not
/// expose them.
/// </summary>
public sealed record UpdatePartyCommand(
    Guid Id,
    PartyVersion ExpectedVersion,
    string LegalName,
    string? DisplayName,
    string? TaxId,
    string? ErpCompanyCode) : ICommand<PartyDetails>;

internal sealed class UpdatePartyValidator : AbstractValidator<UpdatePartyCommand>
{
    public UpdatePartyValidator()
    {
        RuleFor(c => c.LegalName).Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("'legalName' must not be empty.");
    }
}

internal sealed class UpdatePartyHandler : ICommandHandler<UpdatePartyCommand, PartyDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IPartyRepository _parties;

    public UpdatePartyHandler(IRequestTransaction transaction, IPartyRepository parties)
    {
        _transaction = transaction;
        _parties = parties;
    }

    public async Task<Result<PartyDetails>> Handle(UpdatePartyCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var party = await _parties.GetByIdAsync(command.Id, cancellationToken);
        if (party is null)
        {
            return Result.Failure<PartyDetails>(PartyErrors.NotFound(command.Id));
        }

        var current = new PartyVersion(_parties.GetMetadata(party).Version);
        if (current != command.ExpectedVersion)
        {
            return Result.Failure<PartyDetails>(PartyErrors.StaleVersion);
        }

        party.Update(command.LegalName, command.DisplayName, command.TaxId, command.ErpCompanyCode);

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(PartyDetails.From(party, _parties.GetMetadata(party)));
    }
}

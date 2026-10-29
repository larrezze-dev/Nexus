using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Parties.DeactivateParty;

/// <summary>
/// <c>POST /v1/parties/{id}/deactivate</c> (<c>deactivateParty</c>), guarded by <c>If-Match</c>.
/// Idempotent: an already inactive party is returned unchanged, without a new event.
/// </summary>
public sealed record DeactivatePartyCommand(Guid Id, PartyVersion ExpectedVersion) : ICommand<PartyDetails>;

internal sealed class DeactivatePartyHandler : ICommandHandler<DeactivatePartyCommand, PartyDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IPartyRepository _parties;

    public DeactivatePartyHandler(IRequestTransaction transaction, IPartyRepository parties)
    {
        _transaction = transaction;
        _parties = parties;
    }

    public async Task<Result<PartyDetails>> Handle(DeactivatePartyCommand command, CancellationToken cancellationToken)
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

        party.Deactivate();

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(PartyDetails.From(party, _parties.GetMetadata(party)));
    }
}

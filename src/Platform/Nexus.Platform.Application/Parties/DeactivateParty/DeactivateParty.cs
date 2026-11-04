using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Parties.DeactivateParty;

/// <summary>
/// <c>POST /v1/parties/{id}/deactivate</c> (<c>deactivateParty</c>), guarded by <c>If-Match</c>.
/// Idempotent: an already inactive party is returned unchanged, without a new event.
/// <para>
/// Scope (ACC-R10): same rule as <c>updateParty</c> (same <c>PARTY:UPDATE</c> permission): the
/// internal party of a legal entity, business unit or group needs the caller's permission to
/// cover that entity's scope (<see cref="PartyScopeGuard"/>), 403 otherwise.
/// </para>
/// </summary>
public sealed record DeactivatePartyCommand(Guid Id, PartyVersion ExpectedVersion) : ICommand<PartyDetails>;

internal sealed class DeactivatePartyHandler : ICommandHandler<DeactivatePartyCommand, PartyDetails>
{
    private const string UpdatePermission = "PARTY:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IPartyRepository _parties;
    private readonly IPartyReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public DeactivatePartyHandler(
        IRequestTransaction transaction,
        IPartyRepository parties,
        IPartyReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _parties = parties;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<PartyDetails>> Handle(DeactivatePartyCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var party = await _parties.GetByIdAsync(command.Id, cancellationToken);
        if (party is null)
        {
            return Result.Failure<PartyDetails>(PartyErrors.NotFound(command.Id));
        }

        // Before the version check and the idempotent no-op: a caller outside the backed entity's scope learns nothing more.
        if (!await PartyScopeGuard.CoversBackedOrgEntitiesAsync(_readDb, _scopeChecker, UpdatePermission, party.Id, cancellationToken))
        {
            return Result.Failure<PartyDetails>(PartyErrors.ScopeNotCovered);
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

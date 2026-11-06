using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Parties.UpdateParty;

/// <summary>
/// <c>PUT /v1/parties/{id}</c> (<c>updateParty</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>). <c>partyType</c> and <c>isInternal</c> are
/// not here: ORG-R2 keeps them fixed after creation and <c>UpdatePartyRequest</c> does not
/// expose them.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>. If the party is the internal
/// party of a legal entity, business unit or group (ORG-R1), this handler also requires the
/// caller's <c>PARTY:UPDATE</c> to cover that entity's scope (<see cref="PartyScopeGuard"/>) and
/// answers 403 otherwise: editing it is editing the entity's own legal name/tax id/ERP code.
/// Any other party stays unscoped.
/// </para>
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
    private const string UpdatePermission = "PARTY:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly IPartyRepository _parties;
    private readonly IPartyReadDb _readDb;
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public UpdatePartyHandler(
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

    public async Task<Result<PartyDetails>> Handle(UpdatePartyCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var party = await _parties.GetByIdAsync(command.Id, cancellationToken);
        if (party is null)
        {
            return Result.Failure<PartyDetails>(PartyErrors.NotFound(command.Id));
        }

        // Before the version check and any change: a caller outside the backed entity's scope learns nothing more.
        if (!await PartyScopeGuard.CoversBackedOrgEntitiesAsync(_readDb, _scopeChecker, UpdatePermission, party.Id, cancellationToken))
        {
            return Result.Failure<PartyDetails>(PartyErrors.ScopeNotCovered);
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

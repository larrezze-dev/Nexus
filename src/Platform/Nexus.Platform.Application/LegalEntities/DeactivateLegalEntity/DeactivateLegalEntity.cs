using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.LegalEntities.DeactivateLegalEntity;

/// <summary>
/// <c>POST /v1/legal-entities/{id}/deactivate</c> (<c>deactivateLegalEntity</c>), guarded by
/// <c>If-Match</c>. Rule ORG-R5: rejected (422) while the legal entity has at least one active
/// <c>BusinessUnit</c> or <c>Warehouse</c>. Idempotent: an already inactive legal entity is
/// returned unchanged, without a new event (the ORG-R5 check is skipped in that case too, so a
/// second deactivate of an already-inactive legal entity never 422s).
/// </summary>
public sealed record DeactivateLegalEntityCommand(Guid Id, uint ExpectedVersion) : ICommand<LegalEntityDetails>;

internal sealed class DeactivateLegalEntityHandler : ICommandHandler<DeactivateLegalEntityCommand, LegalEntityDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ILegalEntityReadDb _readDb;

    public DeactivateLegalEntityHandler(IRequestTransaction transaction, ILegalEntityRepository legalEntities, ILegalEntityReadDb readDb)
    {
        _transaction = transaction;
        _legalEntities = legalEntities;
        _readDb = readDb;
    }

    public async Task<Result<LegalEntityDetails>> Handle(DeactivateLegalEntityCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var legalEntity = await _legalEntities.GetByIdAsync(command.Id, cancellationToken);
        if (legalEntity is null)
        {
            return Result.Failure<LegalEntityDetails>(LegalEntityErrors.NotFound(command.Id));
        }

        var current = _legalEntities.GetMetadata(legalEntity).Version;
        if (current != command.ExpectedVersion)
        {
            return Result.Failure<LegalEntityDetails>(LegalEntityErrors.StaleVersion);
        }

        if (legalEntity.IsActive)
        {
            // Rule ORG-R5, checked before anything is written.
            var hasActiveChildren = await _readDb.HasActiveChildrenAsync(command.Id, cancellationToken);
            if (hasActiveChildren)
            {
                return Result.Failure<LegalEntityDetails>(LegalEntityErrors.HasActiveChildren);
            }
        }

        var deactivated = legalEntity.Deactivate();
        if (deactivated.IsFailure)
        {
            return Result.Failure<LegalEntityDetails>(deactivated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(LegalEntityDetails.From(legalEntity, _legalEntities.GetMetadata(legalEntity)));
    }
}

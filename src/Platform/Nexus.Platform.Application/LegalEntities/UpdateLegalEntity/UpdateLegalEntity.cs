using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Currencies;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.LegalEntities.UpdateLegalEntity;

/// <summary>
/// <c>PUT /v1/legal-entities/{id}</c> (<c>updateLegalEntity</c>), full replacement guarded by
/// <c>If-Match</c> (<see cref="ExpectedVersion"/>). Never touches the legal entity's party or
/// its <c>groupId</c> (the contract's <c>UpdateLegalEntityRequest</c> has neither).
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers this legal entity (the row's own id
/// is its <c>legal_entity_id</c>: LE, GROUP and GLOBAL grants cover it) and answers 403 otherwise.
/// </para>
/// </summary>
public sealed record UpdateLegalEntityCommand(
    Guid Id,
    uint ExpectedVersion,
    string Code,
    string CurrencyCode,
    string CountryCode) : ICommand<LegalEntityDetails>;

internal sealed class UpdateLegalEntityValidator : AbstractValidator<UpdateLegalEntityCommand>
{
    public UpdateLegalEntityValidator()
    {
        RuleFor(c => c.Code).MustBeNonBlank().Matches(@"^[A-Z0-9_\-\.]{1,40}$");
        RuleFor(c => c.CurrencyCode).Matches(@"^[A-Z]{3}$");
        RuleFor(c => c.CountryCode).Matches(@"^[A-Z]{2}$");
    }
}

internal sealed class UpdateLegalEntityHandler : ICommandHandler<UpdateLegalEntityCommand, LegalEntityDetails>
{
    private const string UpdatePermission = "ORG_STRUCTURE:UPDATE";

    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityRepository _legalEntities;
    private readonly ICurrencyReadDb _currencies;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public UpdateLegalEntityHandler(
        IRequestTransaction transaction,
        ILegalEntityRepository legalEntities,
        ICurrencyReadDb currencies,
        IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _legalEntities = legalEntities;
        _currencies = currencies;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<LegalEntityDetails>> Handle(UpdateLegalEntityCommand command, CancellationToken cancellationToken)
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

        // Before any business rule or change (and before the idempotent no-op on deactivate).
        if (!await _scopeChecker.HasPermissionCoveringAsync(UpdatePermission, "LEGAL_ENTITY", legalEntity.Id, cancellationToken))
        {
            return Result.Failure<LegalEntityDetails>(LegalEntityErrors.ScopeNotCovered);
        }

        // Rule ORG-R7: only re-checked if the currency actually changes.
        if (!string.Equals(legalEntity.CurrencyCode, command.CurrencyCode, StringComparison.Ordinal))
        {
            var currencyCheck = await CurrencyRules.EnsureActiveAsync(_currencies, command.CurrencyCode, cancellationToken);
            if (currencyCheck.IsFailure)
            {
                return Result.Failure<LegalEntityDetails>(currencyCheck.Error!);
            }
        }

        var updated = legalEntity.Update(command.Code, command.CurrencyCode, command.CountryCode);
        if (updated.IsFailure)
        {
            return Result.Failure<LegalEntityDetails>(updated.Error!);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(LegalEntityDetails.From(legalEntity, _legalEntities.GetMetadata(legalEntity)));
    }
}

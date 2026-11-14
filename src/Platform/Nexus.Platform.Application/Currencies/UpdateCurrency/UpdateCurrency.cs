using FluentValidation;
using Nexus.BuildingBlocks.Application.Validation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Currencies.UpdateCurrency;

/// <summary>
/// <c>PUT /v1/currencies/{code}</c> (<c>updateCurrency</c>), guarded by <c>If-Match</c>. The
/// writable fields are <see cref="IsActive"/> and, optionally, <see cref="CaptionI18n"/> (omitted
/// = unchanged; present = replaces the whole map). <c>code</c> and <c>minorUnits</c> are fixed. Rule
/// ORG-R7 is checked here, not in the <c>Currency</c> aggregate: it needs to see
/// <c>org."group"</c>/<c>org.legal_entity</c>, which the aggregate has no way to (see
/// <c>Currency</c>'s remarks).
/// </summary>
public sealed record UpdateCurrencyCommand(string Code, CurrencyVersion ExpectedVersion, bool IsActive, IReadOnlyDictionary<string, string>? CaptionI18n = null) : ICommand<CurrencyDetails>;

internal sealed class UpdateCurrencyValidator : AbstractValidator<UpdateCurrencyCommand>
{
    public UpdateCurrencyValidator()
    {
        RuleFor(c => c.Code)
            // Length(3): .NET's `$` also matches before a trailing "\n" (see CreateCurrencyValidator).
            .Length(3)
            .Matches("^[A-Z]{3}$")
            .WithMessage("'code' must be 3 upper-case letters (ISO 4217).");
        // Optional: null passes (unchanged). Unconditional so the OpenAPI projection shows `de`
        // as required (ADR-0014); same shape rule as MustHaveGermanCaption otherwise.
        RuleFor(c => c.CaptionI18n)
            .MustContainNonBlankKeys("de")
            .WithMessage("'{PropertyName}' must include a non-empty 'de' caption.");
    }
}

internal sealed class UpdateCurrencyHandler : ICommandHandler<UpdateCurrencyCommand, CurrencyDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ICurrencyRepository _currencies;
    private readonly ICurrencyReadDb _readDb;

    public UpdateCurrencyHandler(IRequestTransaction transaction, ICurrencyRepository currencies, ICurrencyReadDb readDb)
    {
        _transaction = transaction;
        _currencies = currencies;
        _readDb = readDb;
    }

    public async Task<Result<CurrencyDetails>> Handle(UpdateCurrencyCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var currency = await _currencies.GetByIdAsync(command.Code, cancellationToken);
        if (currency is null)
        {
            return Result.Failure<CurrencyDetails>(CurrencyErrors.NotFound(command.Code));
        }

        var current = new CurrencyVersion(_currencies.GetMetadata(currency).Version);
        if (current != command.ExpectedVersion)
        {
            return Result.Failure<CurrencyDetails>(CurrencyErrors.StaleVersion);
        }

        if (command.IsActive)
        {
            currency.Activate();
        }
        else
        {
            // Rule ORG-R7: never deactivate a currency a Group/LegalEntity still uses. Only
            // evaluated on an actual active -> inactive transition: renaming an already
            // inactive currency (isActive: false again) must not be blocked.
            if (currency.IsActive && await _readDb.IsReferencedByActiveEntityAsync(command.Code, cancellationToken))
            {
                return Result.Failure<CurrencyDetails>(CurrencyErrors.InUse);
            }

            currency.Deactivate();
        }

        if (command.CaptionI18n is not null)
        {
            currency.Rename(command.CaptionI18n);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(CurrencyDetails.From(currency, _currencies.GetMetadata(currency)));
    }
}

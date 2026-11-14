using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Currencies;

namespace Nexus.Platform.Application.Currencies.CreateCurrency;

/// <summary>
/// <c>POST /v1/currencies</c> (<c>createCurrency</c>). The key is the ISO 4217 code, so there
/// is no client id and no idempotent replay: an existing code is always a 409, also with
/// identical content. Emits no event (org.md §6 defines none for currencies).
/// </summary>
public sealed record CreateCurrencyCommand(
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    short MinorUnits) : ICommand<CurrencyDetails>;

internal sealed class CreateCurrencyValidator : AbstractValidator<CreateCurrencyCommand>
{
    public CreateCurrencyValidator()
    {
        RuleFor(c => c.Code)
            .Matches("^[A-Z]{3}$")
            .WithMessage("'code' must be 3 upper-case letters (ISO 4217).");
        RuleFor(c => c.CaptionI18n).MustHaveGermanCaption();
        RuleFor(c => c.MinorUnits).InclusiveBetween((short)0, (short)4);
    }
}

internal sealed class CreateCurrencyHandler : ICommandHandler<CreateCurrencyCommand, CurrencyDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ICurrencyRepository _currencies;

    public CreateCurrencyHandler(IRequestTransaction transaction, ICurrencyRepository currencies)
    {
        _transaction = transaction;
        _currencies = currencies;
    }

    public async Task<Result<CurrencyDetails>> Handle(CreateCurrencyCommand command, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        if (await _currencies.GetByIdAsync(command.Code, cancellationToken) is not null)
        {
            return Result.Failure<CurrencyDetails>(CurrencyErrors.CodeAlreadyUsed);
        }

        var currency = Currency.Create(command.Code, command.CaptionI18n, command.MinorUnits);
        _currencies.Add(currency);

        // A concurrent create of the same code loses on the primary key; the web layer turns
        // that violation into the same 409 (CurrencyErrors.CurrencyPrimaryKey).
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(CurrencyDetails.From(currency, _currencies.GetMetadata(currency)));
    }
}

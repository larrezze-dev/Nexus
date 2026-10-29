using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Currencies.GetCurrency;

/// <summary><c>GET /v1/currencies/{code}</c> (<c>getCurrency</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetCurrencyQuery(string Code) : IQuery<CurrencyDetails>;

internal sealed class GetCurrencyHandler : IQueryHandler<GetCurrencyQuery, CurrencyDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ICurrencyReadDb _readDb;

    public GetCurrencyHandler(IRequestTransaction transaction, ICurrencyReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<CurrencyDetails>> Handle(GetCurrencyQuery query, CancellationToken cancellationToken)
    {
        // BE-R1: reads go through the unit of work too (RLS session), even on a GLOBAL schema.
        await _transaction.BeginAsync(cancellationToken);

        var currency = await _readDb.GetByCodeAsync(query.Code, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return currency is null
            ? Result.Failure<CurrencyDetails>(CurrencyErrors.NotFound(query.Code))
            : Result.Success(currency);
    }
}

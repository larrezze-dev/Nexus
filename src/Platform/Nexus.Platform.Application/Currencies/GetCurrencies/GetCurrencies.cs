using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Currencies.GetCurrencies;

/// <summary>
/// <c>GET /v1/currencies</c> (<c>listCurrencies</c>): offset pagination (conventions-api.md §4)
/// with the contract's <c>page</c>, <c>pageSize</c> (max 200) and <c>sort</c>
/// (<see cref="CurrencySortFields.Allowed"/>). No alta by API (db/seed/common seeds the four base
/// currencies).
/// </summary>
public sealed record GetCurrenciesQuery(int Page, int PageSize, string? Sort) : IQuery<PagedResult<CurrencyListItem>>;

internal sealed class GetCurrenciesValidator : AbstractValidator<GetCurrenciesQuery>
{
    public GetCurrenciesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, CurrencySortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetCurrenciesHandler : IQueryHandler<GetCurrenciesQuery, PagedResult<CurrencyListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly ICurrencyReadDb _readDb;

    public GetCurrenciesHandler(IRequestTransaction transaction, ICurrencyReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<CurrencyListItem>>> Handle(GetCurrenciesQuery query, CancellationToken cancellationToken)
    {
        // Already validated by GetCurrenciesValidator; parsing again only to obtain the fields.
        SortSpec.TryParse(query.Sort, CurrencySortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

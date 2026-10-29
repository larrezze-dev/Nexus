using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Application.Parties.GetParties;

/// <summary>
/// <c>GET /v1/parties</c> (<c>listParties</c>): offset pagination (conventions-api.md §4) with
/// the contract's <c>page</c>, <c>pageSize</c> (max 200), <c>sort</c>
/// (<see cref="PartySortFields.Allowed"/>) and the <c>partyType</c>/<c>isInternal</c>/<c>q</c>
/// filters.
/// </summary>
public sealed record GetPartiesQuery(
    int Page,
    int PageSize,
    string? Sort,
    string? PartyType,
    bool? IsInternal,
    string? Q) : IQuery<PagedResult<PartyListItem>>;

internal sealed class GetPartiesValidator : AbstractValidator<GetPartiesQuery>
{
    public GetPartiesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, PartySortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
        When(q => q.PartyType is not null, () => RuleFor(q => q.PartyType)
            .Must(value => value is PartyTypeNames.Organization or PartyTypeNames.Person)
            .WithMessage($"'partyType' must be one of: {PartyTypeNames.Organization}, {PartyTypeNames.Person}."));
    }
}

internal sealed class GetPartiesHandler : IQueryHandler<GetPartiesQuery, PagedResult<PartyListItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IPartyReadDb _readDb;

    public GetPartiesHandler(IRequestTransaction transaction, IPartyReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PagedResult<PartyListItem>>> Handle(GetPartiesQuery query, CancellationToken cancellationToken)
    {
        // Already validated by GetPartiesValidator; parsing again only to obtain the fields.
        SortSpec.TryParse(query.Sort, PartySortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);
        var page = await _readDb.ListAsync(query.Page, query.PageSize, sort, query.PartyType, query.IsInternal, query.Q, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}

using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Parties.GetParty;

/// <summary><c>GET /v1/parties/{id}</c> (<c>getParty</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetPartyQuery(Guid Id) : IQuery<PartyDetails>;

internal sealed class GetPartyHandler : IQueryHandler<GetPartyQuery, PartyDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IPartyReadDb _readDb;

    public GetPartyHandler(IRequestTransaction transaction, IPartyReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<PartyDetails>> Handle(GetPartyQuery query, CancellationToken cancellationToken)
    {
        // BE-R1: reads go through the unit of work too (RLS session), even on a GLOBAL schema.
        await _transaction.BeginAsync(cancellationToken);

        var party = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return party is null
            ? Result.Failure<PartyDetails>(PartyErrors.NotFound(query.Id))
            : Result.Success(party);
    }
}

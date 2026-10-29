using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.LegalEntities.GetLegalEntity;

/// <summary><c>GET /v1/legal-entities/{id}</c> (<c>getLegalEntity</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetLegalEntityQuery(Guid Id) : IQuery<LegalEntityDetails>;

internal sealed class GetLegalEntityHandler : IQueryHandler<GetLegalEntityQuery, LegalEntityDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILegalEntityReadDb _readDb;

    public GetLegalEntityHandler(IRequestTransaction transaction, ILegalEntityReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<LegalEntityDetails>> Handle(GetLegalEntityQuery query, CancellationToken cancellationToken)
    {
        // BE-R1: reads go through the unit of work too (RLS session), even on a GLOBAL schema.
        await _transaction.BeginAsync(cancellationToken);

        var legalEntity = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return legalEntity is null
            ? Result.Failure<LegalEntityDetails>(LegalEntityErrors.NotFound(query.Id))
            : Result.Success(legalEntity);
    }
}

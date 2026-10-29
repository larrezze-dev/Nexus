using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Locations.GetLocation;

/// <summary><c>GET /v1/locations/{id}</c> (<c>getLocation</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetLocationQuery(Guid Id) : IQuery<LocationDetails>;

internal sealed class GetLocationHandler : IQueryHandler<GetLocationQuery, LocationDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly ILocationReadDb _readDb;

    public GetLocationHandler(IRequestTransaction transaction, ILocationReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<LocationDetails>> Handle(GetLocationQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var location = await _readDb.GetByIdAsync(query.Id, cancellationToken);

        await _transaction.CommitAsync(cancellationToken);

        return location is null
            ? Result.Failure<LocationDetails>(LocationErrors.NotFound(query.Id))
            : Result.Success(location);
    }
}

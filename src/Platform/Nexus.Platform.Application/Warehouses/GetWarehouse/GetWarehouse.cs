using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Warehouses.GetWarehouse;

/// <summary><c>GET /v1/warehouses/{id}</c> (<c>getWarehouse</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetWarehouseQuery(Guid Id) : IQuery<WarehouseDetails>;

internal sealed class GetWarehouseHandler : IQueryHandler<GetWarehouseQuery, WarehouseDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseReadDb _readDb;

    public GetWarehouseHandler(IRequestTransaction transaction, IWarehouseReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<WarehouseDetails>> Handle(GetWarehouseQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var warehouse = await _readDb.GetByIdAsync(query.Id, cancellationToken);

        await _transaction.CommitAsync(cancellationToken);

        return warehouse is null
            ? Result.Failure<WarehouseDetails>(WarehouseErrors.NotFound(query.Id))
            : Result.Success(warehouse);
    }
}

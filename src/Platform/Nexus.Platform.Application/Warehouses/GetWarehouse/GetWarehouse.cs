using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Warehouses.GetWarehouse;

/// <summary>
/// <c>GET /v1/warehouses/{id}</c> (<c>getWarehouse</c>): detail plus version for the <c>ETag</c>.
/// <para>
/// Scope (ACC-R10): the contract declares <c>x-scope: none</c>, so the permission filter only
/// checks the permission in <i>some</i> scope, and <c>org</c> has no RLS. Once the row is loaded
/// this handler checks that the caller's permission covers the warehouse's own scope
/// (<c>WAREHOUSE</c>, which passes its <c>legal_entity_id</c> and <c>business_unit_id</c> to
/// <c>access.has_permission</c> too: LE/GROUP/GLOBAL grants cover it, and so do narrower grants
/// on the warehouse itself or its business unit) and answers 403 otherwise.
/// </para>
/// </summary>
public sealed record GetWarehouseQuery(Guid Id) : IQuery<WarehouseDetails>;

internal sealed class GetWarehouseHandler : IQueryHandler<GetWarehouseQuery, WarehouseDetails>
{
    private const string ReadPermission = "ORG_STRUCTURE:READ";

    private readonly IRequestTransaction _transaction;
    private readonly IWarehouseReadDb _readDb;

    // IRoleAssignmentScopeChecker carries its historical name (introduced for role assignments),
    // but it is the generic check "does the caller's permission, via access.has_permission, cover
    // this scope" (GLOBAL > GROUP > LEGAL_ENTITY > BUSINESS_UNIT > WAREHOUSE): reused as-is.
    private readonly IRoleAssignmentScopeChecker _scopeChecker;

    public GetWarehouseHandler(IRequestTransaction transaction, IWarehouseReadDb readDb, IRoleAssignmentScopeChecker scopeChecker)
    {
        _transaction = transaction;
        _readDb = readDb;
        _scopeChecker = scopeChecker;
    }

    public async Task<Result<WarehouseDetails>> Handle(GetWarehouseQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var warehouse = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        if (warehouse is null)
        {
            return Result.Failure<WarehouseDetails>(WarehouseErrors.NotFound(query.Id));
        }

        if (!await _scopeChecker.HasPermissionCoveringAsync(ReadPermission, "WAREHOUSE", warehouse.Id, cancellationToken))
        {
            return Result.Failure<WarehouseDetails>(WarehouseErrors.ScopeNotCovered);
        }

        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(warehouse);
    }
}

using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.BusinessUnits.GetBusinessUnit;

/// <summary><c>GET /v1/business-units/{id}</c> (<c>getBusinessUnit</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetBusinessUnitQuery(Guid Id) : IQuery<BusinessUnitDetails>;

internal sealed class GetBusinessUnitHandler : IQueryHandler<GetBusinessUnitQuery, BusinessUnitDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IBusinessUnitReadDb _readDb;

    public GetBusinessUnitHandler(IRequestTransaction transaction, IBusinessUnitReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<BusinessUnitDetails>> Handle(GetBusinessUnitQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var businessUnit = await _readDb.GetByIdAsync(query.Id, cancellationToken);

        await _transaction.CommitAsync(cancellationToken);

        return businessUnit is null
            ? Result.Failure<BusinessUnitDetails>(BusinessUnitErrors.NotFound(query.Id))
            : Result.Success(businessUnit);
    }
}

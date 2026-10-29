using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Groups.GetGroup;

/// <summary><c>GET /v1/groups/{id}</c> (<c>getGroup</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetGroupQuery(Guid Id) : IQuery<GroupDetails>;

internal sealed class GetGroupHandler : IQueryHandler<GetGroupQuery, GroupDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IGroupReadDb _readDb;

    public GetGroupHandler(IRequestTransaction transaction, IGroupReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<GroupDetails>> Handle(GetGroupQuery query, CancellationToken cancellationToken)
    {
        // BE-R1: reads go through the unit of work too (RLS session), even on a GLOBAL schema.
        await _transaction.BeginAsync(cancellationToken);

        var group = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return group is null
            ? Result.Failure<GroupDetails>(GroupErrors.NotFound(query.Id))
            : Result.Success(group);
    }
}

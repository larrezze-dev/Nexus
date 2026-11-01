using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Roles.GetRole;

/// <summary><c>GET /v1/roles/{id}</c> (<c>getRole</c>): detail plus version for the <c>ETag</c>.</summary>
public sealed record GetRoleQuery(Guid Id) : IQuery<RoleDetails>;

internal sealed class GetRoleHandler : IQueryHandler<GetRoleQuery, RoleDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleReadDb _readDb;

    public GetRoleHandler(IRequestTransaction transaction, IRoleReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<RoleDetails>> Handle(GetRoleQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var role = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return role is null
            ? Result.Failure<RoleDetails>(RoleErrors.NotFound(query.Id))
            : Result.Success(role);
    }
}

using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.RoleAssignments.GetRoleAssignment;

/// <summary>
/// <c>GET /v1/role-assignments/{id}</c> (<c>getRoleAssignment</c>): detail plus version for the
/// <c>ETag</c> that <c>revokeRoleAssignment</c>'s <c>If-Match</c> needs.
/// <para>
/// Scope: <c>x-scope: none</c> and, deliberately, no per-row coverage check here (unlike
/// <c>listRoleAssignments</c>): a single lookup by id is broad read visibility for any
/// <c>ROLE_ASSIGNMENT:READ</c> holder, consistent with <c>getUserEffectiveScope</c> (contract).
/// </para>
/// </summary>
public sealed record GetRoleAssignmentQuery(Guid Id) : IQuery<RoleAssignmentDetails>;

internal sealed class GetRoleAssignmentHandler : IQueryHandler<GetRoleAssignmentQuery, RoleAssignmentDetails>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleAssignmentReadDb _readDb;

    public GetRoleAssignmentHandler(IRequestTransaction transaction, IRoleAssignmentReadDb readDb)
    {
        _transaction = transaction;
        _readDb = readDb;
    }

    public async Task<Result<RoleAssignmentDetails>> Handle(GetRoleAssignmentQuery query, CancellationToken cancellationToken)
    {
        await _transaction.BeginAsync(cancellationToken);

        var assignment = await _readDb.GetByIdAsync(query.Id, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return assignment is null
            ? Result.Failure<RoleAssignmentDetails>(RoleAssignmentErrors.NotFound(query.Id))
            : Result.Success(assignment);
    }
}

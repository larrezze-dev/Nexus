using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Domain;
using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Application.RoleAssignments.GetUserEffectiveScope;

/// <summary>
/// <c>GET /v1/users/{id}/effective-scope</c> (<c>getUserEffectiveScope</c>, domain/access.md §4):
/// "what would this user see". Requires the <b>caller's own</b> <c>ROLE_ASSIGNMENT:READ</c>
/// (already checked by the permission filter, <c>x-scope: none</c>) - not the queried user's -
/// this is the one endpoint that must see what a DIFFERENT user would see, not what the caller
/// holding that permission would see. That is exactly why <see cref="IEffectiveScopeReader"/> is
/// its own small piece of infrastructure instead of another read db on the request's own
/// <see cref="IRequestTransaction"/>/<c>IUnitOfWork</c>: that unit of work always stamps
/// <c>app.current_user_id</c> to the caller (<see cref="ICurrentUser"/>), and this query must
/// stamp it to <see cref="GetUserEffectiveScopeQuery.UserId"/> instead.
/// </summary>
public sealed record GetUserEffectiveScopeQuery(Guid UserId) : IQuery<EffectiveScope>;

internal sealed class GetUserEffectiveScopeHandler : IQueryHandler<GetUserEffectiveScopeQuery, EffectiveScope>
{
    private readonly IRequestTransaction _transaction;
    private readonly IUserReadDb _users;
    private readonly IEffectiveScopeReader _effectiveScope;

    public GetUserEffectiveScopeHandler(IRequestTransaction transaction, IUserReadDb users, IEffectiveScopeReader effectiveScope)
    {
        _transaction = transaction;
        _users = users;
        _effectiveScope = effectiveScope;
    }

    public async Task<Result<EffectiveScope>> Handle(GetUserEffectiveScopeQuery query, CancellationToken cancellationToken)
    {
        // This unit of work is only used to confirm the queried user exists (404 otherwise), on
        // the caller's own RLS session - it never touches access.effective_access_scope itself.
        await _transaction.BeginAsync(cancellationToken);
        var user = await _users.GetByIdAsync(query.UserId, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        if (user is null)
        {
            return Result.Failure<EffectiveScope>(Error.NotFound($"User '{query.UserId}' does not exist."));
        }

        // Deliberately not on the unit of work above: IEffectiveScopeReader opens its own
        // read-only transaction impersonating query.UserId (see its implementation).
        var scope = await _effectiveScope.ReadAsync(query.UserId, cancellationToken);
        return Result.Success(scope);
    }
}

using Nexus.BuildingBlocks.Application;

namespace Nexus.Platform.Application.Users;

/// <summary>
/// Scope check of the user write endpoints (<c>updateUser</c>, <c>deactivateUser</c>, both
/// <c>USER:UPDATE</c>, <c>x-scope: none</c>). A user has no single owning scope, but a user who
/// holds role assignments carries authority in those scopes: editing or deactivating them is
/// acting on that authority (an admin of one legal entity relabelling, or locking out for good,
/// the admin of another). So the caller's permission must cover <b>every</b> scope the target
/// holds an assignment in that has not ended (<see cref="IUserReadDb.ListUnendedAssignmentScopesAsync"/>),
/// the same "cover every backed scope" pattern as the party writes. A target with no
/// assignment (most users) has nothing to cover: unrestricted, as before.
/// </summary>
internal static class UserScopeGuard
{
    public static async Task<bool> CoversEveryAssignmentScopeAsync(
        IUserReadDb readDb,
        IRoleAssignmentScopeChecker scopeChecker,
        string permission,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var scopes = await readDb.ListUnendedAssignmentScopesAsync(targetUserId, cancellationToken);
        foreach (var scope in scopes)
        {
            // GLOBAL (no entity id) resolves to a target only a GLOBAL grant covers.
            if (!await scopeChecker.HasPermissionCoveringAsync(permission, scope.ScopeType, scope.ScopeEntityId, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }
}

namespace Nexus.BuildingBlocks.Application;

/// <summary>
/// Asks <c>access.has_permission</c> whether the current caller holds a permission in a scope
/// that <b>covers</b> a role-assignment scope (<c>scopeType</c>/<c>scopeEntityId</c>), following
/// the hierarchy GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY ⊃ BUSINESS_UNIT ⊃ WAREHOUSE (domain/access.md
/// §4, ACC-R4).
/// <para>
/// The permission filter already answers this for <c>POST /v1/role-assignments</c>
/// (<c>x-scope: body.scopeEntityId</c>, scope read from the body). This port exists for the
/// handlers that only learn the target scope once they have loaded a row (e.g.
/// <c>revokeRoleAssignment</c>, ACC-R10: write endpoints check the row's own scope). Its
/// implementation is the same <c>AccessPermissionChecker</c>/<c>RoleAssignmentScopeResolver</c>
/// pair the filter uses (Nexus.BuildingBlocks.Web), so both paths always give the same answer
/// for the same scope. Like <see cref="ICurrentUser"/>, it lives here so application handlers can
/// depend on it without referencing the web layer.
/// </para>
/// Runs inside the request's own transaction (same <c>app.current_user_id</c>).
/// </summary>
public interface IRoleAssignmentScopeChecker
{
    /// <summary>
    /// <see langword="true"/> if the caller holds <paramref name="permission"/> in a scope that
    /// covers <paramref name="scopeType"/>/<paramref name="scopeEntityId"/>; fails closed
    /// (<see langword="false"/>) on anything it cannot resolve.
    /// </summary>
    Task<bool> HasPermissionCoveringAsync(
        string permission,
        string scopeType,
        Guid? scopeEntityId,
        CancellationToken cancellationToken);
}

namespace Nexus.BuildingBlocks.Analytics.Scope;

/// <summary>
/// Computes a <see cref="ScopeFilter"/> for the current user in PostgreSQL, on the request's own
/// <c>IUnitOfWork</c> connection and transaction (conventions-api.md §7.2), so it sees exactly the
/// same <c>app.current_user_id</c> and the same snapshot as the permission check and the handler.
/// <para>
/// Scoped (one per request). Both methods throw <see cref="InvalidOperationException"/> when the
/// request's unit of work is not active: a scope computed outside the request transaction would
/// not be the one the authorization layer evaluated. In an HTTP endpoint with
/// <c>x-permission</c> the permission filter has already begun it.
/// </para>
/// </summary>
public interface IScopeFilterProvider
{
    /// <summary>
    /// The general analytic scope: <c>access.visible_legal_entities()</c>, verbatim.
    /// <see cref="ScopeFilter.IncludeUnscoped"/> is always <see langword="false"/>.
    /// </summary>
    Task<ScopeFilter> VisibleLegalEntitiesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The scope of a permission (the audit semantics, conventions-api.md §7 <c>[+]</c>):
    /// <list type="bullet">
    ///   <item>Legal entities: every <c>org.legal_entity</c> row for which
    ///   <c>access.has_permission(permission, id)</c> holds, i.e. the permission is granted by a
    ///   <c>GLOBAL</c> assignment, a <c>GROUP</c> assignment on the legal entity's group, or a
    ///   <c>LEGAL_ENTITY</c> assignment on it. Assignments scoped to a business unit, warehouse or
    ///   project do <b>not</b> grant a legal entity (they cover only a part of it), so a user
    ///   holding the permission only there gets no legal entity from this method. That is the
    ///   intended semantics, the same as the 0.6 organisation listings.</item>
    ///   <item><see cref="ScopeFilter.IncludeUnscoped"/>: the permission is held in a
    ///   <c>GLOBAL</c> assignment.</item>
    /// </list>
    /// </summary>
    /// <param name="permission">Permission code, e.g. <c>AUDIT:READ</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ScopeFilter> ForPermissionAsync(string permission, CancellationToken cancellationToken);
}

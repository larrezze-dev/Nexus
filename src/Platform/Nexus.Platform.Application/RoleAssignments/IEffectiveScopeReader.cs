namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>
/// <c>GET /v1/users/{id}/effective-scope</c>'s special-cased read (domain/access.md §4): reads
/// <c>access.effective_access_scope</c> as the <b>queried</b> user, not the caller - the one
/// place in this service where a different identity than <see cref="Nexus.BuildingBlocks.Application.ICurrentUser"/>
/// must drive the RLS/scope session. See <c>EffectiveScopeReader</c>
/// (<c>Nexus.Platform.Infrastructure</c>) for why this bypasses the normal
/// <see cref="Nexus.BuildingBlocks.Application.IRequestTransaction"/>/<c>IUnitOfWork</c> pattern
/// entirely instead of being another read db on the request's own transaction.
/// </summary>
public interface IEffectiveScopeReader
{
    /// <summary>
    /// The effective scope of <paramref name="userId"/>, grouped by dimension. There is no
    /// "user not found" outcome here by design: an unknown or inactive user simply has an empty
    /// scope in every dimension (the view returns nothing for them), which is a true answer to
    /// "what would this user see", not an error - the caller (the query handler) still checks the
    /// user's existence itself before calling this, for the 404 the endpoint promises.
    /// </summary>
    Task<EffectiveScope> ReadAsync(Guid userId, CancellationToken cancellationToken);
}

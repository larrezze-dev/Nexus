using Nexus.BuildingBlocks.Application.Paging;

namespace Nexus.Platform.Application.Users;

/// <summary>
/// Read side of users (conventions-backend.md §1: Dapper on the unit of work's connection and
/// transaction; BE-R1). Returns flat read models, never aggregates. Requires an active
/// transaction.
/// </summary>
public interface IUserReadDb
{
    Task<UserDetails?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of users ordered by <paramref name="sort"/> (fields of
    /// <see cref="UserSortFields.Allowed"/>), then by id for a stable order. An empty
    /// <paramref name="sort"/> orders by full name.
    /// </summary>
    Task<PagedResult<UserListItem>> ListAsync(int page, int pageSize, IReadOnlyList<SortField> sort, CancellationToken cancellationToken);

    /// <summary>The profile <c>GET /v1/me</c> needs, for an active user; <see langword="null"/> otherwise.</summary>
    Task<CurrentUserProfile?> GetActiveProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The distinct scopes (<c>scope_type</c>/<c>scope_entity_id</c>) of every role assignment of
    /// <paramref name="userId"/> that has not ended yet (<c>valid_to</c> NULL or in the future):
    /// the ones in force now <b>and</b> the ones scheduled to start later, whatever the state of
    /// the role. Wider than <c>access.active_assignments()</c> on purpose: it feeds a
    /// "the caller must cover this user's authority" check, and a grant starting tomorrow (or a
    /// role reactivated tomorrow) is authority the user is about to have. Empty for a user with
    /// no assignment (most users).
    /// </summary>
    Task<IReadOnlyList<UserAssignmentScope>> ListUnendedAssignmentScopesAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>The NEXUS part of <c>GET /v1/me</c> (the rest comes from the token). Since step 0.6,
/// <see cref="Permissions"/> carries the caller's currently granted permissions, grouped by code.</summary>
public sealed record CurrentUserProfile(
    Guid UserId,
    Guid PartyId,
    Guid? BusinessUnitId,
    string PreferredLanguage,
    IReadOnlyList<CurrentUserPermissionGrant> Permissions);

/// <summary>One permission the caller currently holds, with every scope that grants it.</summary>
public sealed record CurrentUserPermissionGrant(string Code, IReadOnlyList<CurrentUserPermissionScope> Scopes);

/// <summary>A scope (`access.role_assignment.scope_type`/`scope_entity_id`) granting a permission.</summary>
public sealed record CurrentUserPermissionScope(string ScopeType, Guid? ScopeEntityId);

/// <summary>A scope a user holds a role assignment in (<c>GLOBAL</c> has no entity id).</summary>
public sealed record UserAssignmentScope(string ScopeType, Guid? ScopeEntityId);

/// <summary>Sortable fields of <c>GET /v1/users</c> (API names).</summary>
public static class UserSortFields
{
    public const string FullName = "fullName";
    public const string Email = "email";
    public const string PreferredLanguage = "preferredLanguage";
    public const string IsActive = "isActive";
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static IReadOnlySet<string> Allowed { get; } = new HashSet<string>(
        [FullName, Email, PreferredLanguage, IsActive, CreatedAt, UpdatedAt],
        StringComparer.Ordinal);
}

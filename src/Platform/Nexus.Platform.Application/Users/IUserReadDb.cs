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
}

/// <summary>The NEXUS part of <c>GET /v1/me</c> (the rest comes from the token).</summary>
public sealed record CurrentUserProfile(Guid UserId, Guid PartyId, Guid? BusinessUnitId, string PreferredLanguage);

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

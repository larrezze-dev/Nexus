using Nexus.Platform.Application.Users;

namespace Nexus.Platform.Api.Users;

// HTTP shapes of contracts/openapi/platform.yaml (Users). PascalCase here, camelCase on the
// wire (JsonSerializerDefaults.Web). `required` marks the contract's required request
// properties: the JSON binder rejects a body without them (400) and the generated OpenAPI
// document lists them as required, like the contract.

/// <summary><c>CreateUserRequest</c>.</summary>
public sealed record CreateUserRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required string Oid { get; init; }

    public required string FullName { get; init; }

    public string? Email { get; init; }

    public required string PreferredLanguage { get; init; }

    public Guid? BusinessUnitId { get; init; }
}

/// <summary><c>UpdateUserRequest</c> (full replacement: an omitted <c>oid</c> is <c>null</c>).</summary>
public sealed record UpdateUserRequest
{
    public string? Oid { get; init; }

    public required string FullName { get; init; }

    public string? Email { get; init; }

    public required string PreferredLanguage { get; init; }

    public Guid? BusinessUnitId { get; init; }
}

/// <summary><c>User</c>.</summary>
public sealed record UserResponse(
    Guid Id,
    Guid PartyId,
    string? Oid,
    string FullName,
    string? Email,
    string PreferredLanguage,
    Guid? BusinessUnitId,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static UserResponse From(UserDetails user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserResponse(
            user.Id, user.PartyId, user.Oid, user.FullName, user.Email, user.PreferredLanguage,
            user.BusinessUnitId, user.IsActive, user.CreatedAt, user.UpdatedAt);
    }
}

/// <summary><c>UserListItem</c>.</summary>
public sealed record UserListItemResponse(
    Guid Id,
    string? Oid,
    string FullName,
    string? Email,
    string PreferredLanguage,
    Guid? BusinessUnitId,
    bool IsActive);

/// <summary>The shared <c>Page</c> schema with typed items.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

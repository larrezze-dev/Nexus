using Nexus.Platform.Application.Permissions;
using Nexus.Platform.Application.Roles;

namespace Nexus.Platform.Api.Roles;

// HTTP shapes of contracts/openapi/platform.yaml (tag Roles). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web).

/// <summary><c>Permission</c>.</summary>
public sealed record PermissionResponse(string Code, string ResourceType, string Action, IReadOnlyDictionary<string, string> CaptionI18n)
{
    public static PermissionResponse From(PermissionItem item) =>
        new(item.Code, item.ResourceType, item.Action, item.CaptionI18n);
}

/// <summary><c>PermissionGrant</c>.</summary>
public sealed record PermissionGrantResponse(
    string Code,
    string ResourceType,
    string Action,
    IReadOnlyDictionary<string, string> CaptionI18n,
    DateTime ValidFrom,
    DateTime? ValidTo)
{
    public static PermissionGrantResponse From(PermissionGrantItem item) =>
        new(item.Code, item.ResourceType, item.Action, item.CaptionI18n, item.ValidFrom, item.ValidTo);
}

/// <summary><c>CreateRoleRequest</c>.</summary>
public sealed record CreateRoleRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required string Code { get; init; }

    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }

    public string? Description { get; init; }
}

/// <summary><c>UpdateRoleRequest</c>: <c>code</c> and <c>isSystem</c> are immutable and not exposed here.</summary>
public sealed record UpdateRoleRequest
{
    public required IReadOnlyDictionary<string, string> CaptionI18n { get; init; }

    public string? Description { get; init; }
}

/// <summary><c>UpdateRolePermissionsRequest</c>.</summary>
public sealed record UpdateRolePermissionsRequest
{
    public required IReadOnlyList<string> PermissionCodes { get; init; }
}

/// <summary><c>Role</c>.</summary>
public sealed record RoleResponse(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? Description,
    bool IsSystem,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static RoleResponse From(RoleDetails role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new RoleResponse(
            role.Id, role.Code, role.CaptionI18n, role.Description, role.IsSystem, role.IsActive, role.CreatedAt, role.UpdatedAt);
    }
}

/// <summary><c>RoleListItem</c>.</summary>
public sealed record RoleListItemResponse(Guid Id, string Code, IReadOnlyDictionary<string, string> CaptionI18n, bool IsSystem, bool IsActive);

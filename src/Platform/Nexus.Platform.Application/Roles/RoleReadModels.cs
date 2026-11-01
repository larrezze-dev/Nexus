using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Application.Roles;

/// <summary>Detail read model of a role (<c>Role</c> schema) plus its <see cref="Version"/> for the <c>ETag</c>.</summary>
public sealed record RoleDetails(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? Description,
    bool IsSystem,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static RoleDetails From(Role role, RowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(role);

        return new RoleDetails(
            role.Id, role.Code, role.CaptionI18n, role.Description, role.IsSystem, role.IsActive,
            metadata.CreatedAt.UtcDateTime, metadata.UpdatedAt.UtcDateTime, metadata.Version);
    }
}

/// <summary>List read model of a role (<c>RoleListItem</c> schema).</summary>
public sealed record RoleListItem(Guid Id, string Code, IReadOnlyDictionary<string, string> CaptionI18n, bool IsSystem, bool IsActive);

/// <summary>
/// One granted permission of a role, with its validity window (<c>PermissionGrant</c> schema:
/// <c>GET/PUT /v1/roles/{id}/permissions</c>).
/// </summary>
public sealed record PermissionGrantItem(
    string Code,
    string ResourceType,
    string Action,
    IReadOnlyDictionary<string, string> CaptionI18n,
    DateTime ValidFrom,
    DateTime? ValidTo);

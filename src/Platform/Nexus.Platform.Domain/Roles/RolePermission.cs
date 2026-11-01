using System.Diagnostics.CodeAnalysis;

namespace Nexus.Platform.Domain.Roles;

/// <summary>
/// <c>access.role_permission</c> (domain/access.md §3): one validity window of one permission
/// granted to one <see cref="Role"/> (A10 temporal pattern). Not an <see cref="Nexus.BuildingBlocks.Domain.AggregateRoot{TId}"/>:
/// it raises no domain events of its own (<c>RoleUpdated</c> is raised on the owning
/// <see cref="Role"/> instead, by the <c>UpdateRolePermissions</c> handler) and has no
/// <c>created_at</c>/<c>updated_at</c> (the DDL comment: "<c>role_permission</c> no las lleva,
/// es su propia forma temporal"). Rows are never updated except to close them
/// (<see cref="Close"/>, ACC-R7); a new grant is always a new row.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "'RolePermission' names the access.role_permission table (domain/access.md §3), " +
                    "not a flags enum; the analyzer's suffix heuristic does not apply here.")]
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    public Guid Id { get; private init; }

    public Guid RoleId { get; private init; }

    public Guid PermissionId { get; private init; }

    public DateTimeOffset ValidFrom { get; private init; }

    public DateTimeOffset? ValidTo { get; private set; }

    public Guid GrantedBy { get; private init; }

    /// <summary>Opens a new, currently-valid grant.</summary>
    public static RolePermission Open(Guid id, Guid roleId, Guid permissionId, DateTimeOffset validFrom, Guid grantedBy) =>
        new()
        {
            Id = id,
            RoleId = roleId,
            PermissionId = permissionId,
            ValidFrom = validFrom,
            ValidTo = null,
            GrantedBy = grantedBy,
        };

    /// <summary>Rule ACC-R7: closes this grant's validity window; never touches <see cref="PermissionId"/>.</summary>
    public void Close(DateTimeOffset validTo) => ValidTo = validTo;
}

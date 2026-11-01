using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Application.Roles;

/// <summary>
/// Write-side access to <see cref="RolePermission"/> rows (implemented over
/// <c>AccessDbContext</c>). Rule ACC-R7: a permission change on a role is always an
/// <see cref="Add"/> of a new row or a <see cref="RolePermission.Close"/> on an existing one,
/// never an <c>UPDATE</c> of <c>permission_id</c>.
/// </summary>
public interface IRolePermissionRepository
{
    /// <summary>Tracks a newly opened grant; it is inserted on commit.</summary>
    void Add(RolePermission rolePermission);

    /// <summary>
    /// Loads, tracked (for <see cref="RolePermission.Close"/>), the currently-valid
    /// (<c>valid_to IS NULL</c>) grants of <paramref name="roleId"/> whose <c>permissionId</c>
    /// is one of <paramref name="permissionIds"/>.
    /// </summary>
    Task<IReadOnlyList<RolePermission>> GetOpenAsync(Guid roleId, IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken);
}

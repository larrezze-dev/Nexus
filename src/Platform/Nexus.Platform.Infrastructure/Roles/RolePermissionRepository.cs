using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Roles;
using Nexus.Platform.Domain.Roles;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Roles;

internal sealed class RolePermissionRepository : IRolePermissionRepository
{
    private readonly AccessDbContext _db;

    public RolePermissionRepository(AccessDbContext db)
    {
        _db = db;
    }

    public void Add(RolePermission rolePermission) => _db.RolePermissions.Add(rolePermission);

    public async Task<IReadOnlyList<RolePermission>> GetOpenAsync(
        Guid roleId, IReadOnlyCollection<Guid> permissionIds, CancellationToken cancellationToken) =>
        await _db.RolePermissions
            .Where(rp => rp.RoleId == roleId && rp.ValidTo == null && permissionIds.Contains(rp.PermissionId))
            .ToListAsync(cancellationToken);
}

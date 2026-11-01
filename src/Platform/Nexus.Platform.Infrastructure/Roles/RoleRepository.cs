using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Roles;
using Nexus.Platform.Domain.Roles;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Roles;

internal sealed class RoleRepository : IRoleRepository
{
    private readonly AccessDbContext _db;

    public RoleRepository(AccessDbContext db)
    {
        _db = db;
    }

    public void Add(Role role) => _db.Roles.Add(role);

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Roles.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public RowMetadata GetMetadata(Role role) => OrgRowMetadata.Read(_db.Entry(role));
}

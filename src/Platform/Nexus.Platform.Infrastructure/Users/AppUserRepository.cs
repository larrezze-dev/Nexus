using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Users;
using Nexus.Platform.Domain.Users;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Users;

internal sealed class AppUserRepository : IAppUserRepository
{
    private readonly OrgDbContext _db;

    public AppUserRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(AppUser user) => _db.AppUsers.Add(user);

    public Task<AppUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.AppUsers.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public RowMetadata GetMetadata(AppUser user) => OrgRowMetadata.Read(_db.Entry(user));
}

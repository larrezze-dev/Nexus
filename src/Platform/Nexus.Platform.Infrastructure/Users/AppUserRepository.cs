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

    public async Task<AppUser?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Two statements on purpose. The first one waits for any concurrent writer of the row
        // and then holds its lock until this transaction ends. The second one is an ordinary EF
        // load: under READ COMMITTED (the server default; UnitOfWork does not change it) each
        // statement takes a fresh snapshot, so it reads the row as the previous writer committed
        // it, with that writer's xmin and aggregate_version, and the concurrency check on save
        // cannot fail against another caller of this method. Callers must not have loaded this
        // user into the context before (EF would return the stale tracked instance).
        // FOR NO KEY UPDATE, not FOR UPDATE: it is the lock an UPDATE of non-key columns takes
        // anyway, and it does not block the FOR KEY SHARE lock a concurrent insert into a table
        // referencing app_user(id) takes (access.role_assignment.user_id).
        // Single row, locked first in the transaction: no lock-ordering deadlock with it.
        await _db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM org.app_user WHERE id = {id} FOR NO KEY UPDATE",
            cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public RowMetadata GetMetadata(AppUser user) => OrgRowMetadata.Read(_db.Entry(user));
}

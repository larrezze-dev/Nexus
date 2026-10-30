using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.Groups;
using Nexus.Platform.Domain.Groups;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.Groups;

internal sealed class GroupRepository : IGroupRepository
{
    private readonly OrgDbContext _db;

    public GroupRepository(OrgDbContext db)
    {
        _db = db;
    }

    public void Add(Group group) => _db.Groups.Add(group);

    public Task<Group?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

    public RowMetadata GetMetadata(Group group) => OrgRowMetadata.Read(_db.Entry(group));
}

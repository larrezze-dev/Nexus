using Microsoft.EntityFrameworkCore;
using Nexus.Platform.Application.Common;
using Nexus.Platform.Application.RoleAssignments;
using Nexus.Platform.Domain.RoleAssignments;
using Nexus.Platform.Infrastructure.Persistence;

namespace Nexus.Platform.Infrastructure.RoleAssignments;

internal sealed class RoleAssignmentRepository : IRoleAssignmentRepository
{
    private readonly RoleAssignmentDbContext _db;

    public RoleAssignmentRepository(RoleAssignmentDbContext db)
    {
        _db = db;
    }

    public void Add(RoleAssignment assignment) => _db.RoleAssignments.Add(assignment);

    public Task<RoleAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.RoleAssignments.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public RowMetadata GetMetadata(RoleAssignment assignment) => OrgRowMetadata.Read(_db.Entry(assignment));
}

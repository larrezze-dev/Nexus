using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Infrastructure.Persistence;

/// <summary>
/// Write model of the <c>access</c> schema (EF Core, conventions-backend.md §1/§4; domain/access.md
/// §3). Same contract as <see cref="OrgDbContext"/> (runs on the request's unit of work,
/// snake_case names, <c>xmin</c> concurrency, no direct <c>SaveChanges</c>, no EF migrations -
/// the tables come from <c>db/migrations/V202610300800__access__core.sql</c>), kept as its own
/// context (rather than folded into <see cref="OrgDbContext"/>) because it has its own default
/// schema: <c>access</c> is a different schema from <c>org</c> (ARC-R1), with no RLS
/// (domain/access.md §4 preamble).
/// <para>
/// Only <see cref="Role"/> and <see cref="RolePermission"/> today (step 0.6, Roles track):
/// <c>access.permission</c> is a read-only seeded catalog (Dapper-only,
/// <c>Nexus.Platform.Infrastructure.Permissions.PermissionReadDb</c>) and
/// <c>access.role_assignment</c> belongs to the parallel RoleAssignments track.
/// </para>
/// </summary>
public sealed class AccessDbContext : DbContextBase
{
    public const string Schema = "access";

    private readonly AuditStampingInterceptor _auditStamping;

    public AccessDbContext(IUnitOfWork unitOfWork, AuditStampingInterceptor auditStamping)
        : base(unitOfWork)
    {
        _auditStamping = auditStamping;
    }

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    protected override void ConfigureOptions(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.AddInterceptors(_auditStamping);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        // Not ".Configurations" under this type's own namespace (the convention OrgDbContext
        // uses): that resolves to the exact same folder/namespace OrgDbContext already scans
        // (both DbContexts live in Nexus.Platform.Infrastructure.Persistence), which would also
        // add every org.* entity to this access-schema model. A dedicated sibling namespace
        // keeps the two completely separate.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AccessDbContext).Assembly,
            type => type.Namespace == typeof(AccessDbContext).Namespace + ".AccessConfigurations");
    }
}

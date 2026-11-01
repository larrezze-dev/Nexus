using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.RoleAssignments;

namespace Nexus.Platform.Infrastructure.RoleAssignments;

/// <summary>
/// Write model of <c>access.role_assignment</c> (EF Core, conventions-backend.md §1/§4): runs on
/// the request's unit of work (connection, transaction, RLS session), snake_case names,
/// <c>xmin</c> concurrency (<see cref="DbContextBase"/>), no direct <c>SaveChanges</c> (BE-R2),
/// no EF migrations (ADR-0001: the table comes from
/// <c>db/migrations/V202610300800__access__core.sql</c>).
/// <para>
/// Deliberately its own <see cref="DbContext"/>, separate from the platform's <c>OrgDbContext</c>
/// (which only covers the <c>org</c> schema) and from whatever write model the <c>Roles</c>
/// feature adds for <c>access.role</c>/<c>access.role_permission</c> (built in parallel, step
/// 0.6-B): a service can enlist any number of <see cref="DbContextBase"/> instances in the same
/// <see cref="IUnitOfWork"/> (they share its connection/transaction and are all saved by the same
/// <c>CommitAsync</c>), so there is no need for one shared "access schema" context, and keeping
/// this one self-contained avoids two features racing to edit the same file.
/// </para>
/// </summary>
public sealed class RoleAssignmentDbContext : DbContextBase
{
    public const string Schema = "access";

    private readonly AuditStampingInterceptor _auditStamping;

    public RoleAssignmentDbContext(IUnitOfWork unitOfWork, AuditStampingInterceptor auditStamping)
        : base(unitOfWork)
    {
        _auditStamping = auditStamping;
    }

    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();

    protected override void ConfigureOptions(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.AddInterceptors(_auditStamping);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // Applied explicitly (not ApplyConfigurationsFromAssembly over a shared namespace):
        // this context must only ever know about RoleAssignmentConfiguration, never pick up an
        // IEntityTypeConfiguration<T> the parallel Roles feature adds for access.role/
        // access.role_permission (or vice versa) just because both live under
        // Nexus.Platform.Infrastructure.
        modelBuilder.ApplyConfiguration(new RoleAssignmentConfiguration());
    }
}

/// <summary><c>access.role_assignment</c>.</summary>
internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role_assignment");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ValidFrom).HasColumnType("timestamptz");
        builder.Property(e => e.ValidTo).HasColumnType("timestamptz");

        // No navigations: user_id/role_id are plain FK columns (mapped by convention, no
        // IEntityTypeConfiguration for org.app_user/access.role in this context) that the
        // application never joins through - this table is its own aggregate.
        builder.HasStandardAuditColumns();
    }
}

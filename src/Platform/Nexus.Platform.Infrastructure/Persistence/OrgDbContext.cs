using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Domain.Currencies;
using Nexus.Platform.Domain.Groups;
using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Domain.Locations;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;
using Nexus.Platform.Domain.Warehouses;

namespace Nexus.Platform.Infrastructure.Persistence;

/// <summary>
/// Write model of the <c>org</c> schema (EF Core, conventions-backend.md §1/§4). Inherits from
/// <see cref="DbContextBase"/>: runs on the request's unit of work (connection, transaction,
/// RLS session), snake_case names, <c>xmin</c> concurrency on every entity, no direct
/// <c>SaveChanges</c> (BE-R2), no EF migrations (ADR-0001: the tables come from
/// <c>db/migrations/V202610241000__org__core.sql</c>).
/// <para>
/// How to add or extend an entity: one <see cref="IEntityTypeConfiguration{TEntity}"/> per
/// entity in <c>Persistence/Configurations/</c> (picked up automatically by
/// <see cref="ModelBuilder.ApplyConfigurationsFromAssembly"/>), with an explicit
/// <c>ToTable("&lt;table&gt;")</c> and <c>HasStandardAuditColumns()</c>; columns follow from
/// the snake_case convention. The <see cref="DbSet{TEntity}"/> properties below are
/// conveniences for repositories; there is no other shared file to edit.
/// </para>
/// Registered as scoped (one per request, enlisted in that request's unit of work).
/// </summary>
public sealed class OrgDbContext : DbContextBase
{
    public const string Schema = "org";

    private readonly AuditStampingInterceptor _auditStamping;

    public OrgDbContext(IUnitOfWork unitOfWork, AuditStampingInterceptor auditStamping)
        : base(unitOfWork)
    {
        _auditStamping = auditStamping;
    }

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();

    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    protected override void ConfigureOptions(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);
        optionsBuilder.AddInterceptors(_auditStamping);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(OrgDbContext).Assembly,
            type => type.Namespace == typeof(OrgDbContext).Namespace + ".Configurations");
    }
}

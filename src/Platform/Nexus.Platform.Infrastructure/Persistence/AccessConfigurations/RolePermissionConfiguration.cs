using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.Platform.Domain.Roles;

namespace Nexus.Platform.Infrastructure.Persistence.AccessConfigurations;

/// <summary>
/// <c>access.role_permission</c> (A10 temporal shape: no standard audit columns, unlike
/// <see cref="RoleConfiguration"/> - see the DDL comment on <c>db/migrations/V202610300800__access__core.sql</c>).
/// Not an aggregate root (<see cref="Nexus.BuildingBlocks.Domain.AggregateRoot{TId}"/>): plain
/// entity, no domain events of its own.
/// </summary>
internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role_permission");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // No navigations: Role/Permission are separate aggregates/read-only catalog respectively.
        builder.HasOne<Role>().WithMany().HasForeignKey(e => e.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

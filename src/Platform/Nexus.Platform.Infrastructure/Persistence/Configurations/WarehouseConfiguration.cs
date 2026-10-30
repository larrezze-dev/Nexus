using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Domain.Locations;
using Nexus.Platform.Domain.Warehouses;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.warehouse</c>.</summary>
internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("warehouse");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CaptionI18n).IsI18nText();

        // No navigations (separate aggregates); declared for FK metadata/insert ordering
        // (consistent with the other org configurations) even though a Warehouse never
        // creates its Location/LegalEntity/BusinessUnit in the same use case (ORG-R3).
        builder.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(e => e.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessUnit>().WithMany().HasForeignKey(e => e.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

        builder.HasStandardAuditColumns();
    }
}

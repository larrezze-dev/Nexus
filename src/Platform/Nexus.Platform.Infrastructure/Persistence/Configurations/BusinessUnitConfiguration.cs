using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.business_unit</c>.</summary>
internal sealed class BusinessUnitConfiguration : IEntityTypeConfiguration<BusinessUnit>
{
    public void Configure(EntityTypeBuilder<BusinessUnit> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("business_unit");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CaptionI18n).IsI18nText();

        // No navigations (separate aggregates); declared so SaveChanges inserts the Party
        // this BusinessUnit's create use case creates inline (ORG-R1) before the BU itself.
        builder.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LegalEntity>().WithMany().HasForeignKey(e => e.LegalEntityId).OnDelete(DeleteBehavior.Restrict);

        builder.HasStandardAuditColumns();
    }
}

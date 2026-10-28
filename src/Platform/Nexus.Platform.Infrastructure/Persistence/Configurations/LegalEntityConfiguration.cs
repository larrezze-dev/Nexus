using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Currencies;
using Nexus.Platform.Domain.Groups;
using Nexus.Platform.Domain.LegalEntities;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.legal_entity</c>.</summary>
internal sealed class LegalEntityConfiguration : IEntityTypeConfiguration<LegalEntity>
{
    public void Configure(EntityTypeBuilder<LegalEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("legal_entity");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CurrencyCode).HasColumnType("char(3)");
        builder.Property(e => e.CountryCode).HasColumnType("char(2)");

        // No navigations (separate aggregates); declared so SaveChanges inserts the Party
        // this LegalEntity's create use case creates inline (ORG-R1) before the LE itself.
        builder.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany().HasForeignKey(e => e.CurrencyCode).OnDelete(DeleteBehavior.Restrict);

        builder.HasStandardAuditColumns();
    }
}

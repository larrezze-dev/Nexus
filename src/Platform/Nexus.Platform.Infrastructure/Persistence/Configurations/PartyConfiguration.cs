using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.party</c> (<c>party_type</c> stored as <c>ORGANIZATION</c>/<c>PERSON</c>).</summary>
internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("party");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PartyType)
            .HasConversion(value => value.ToName(), name => PartyTypeNames.Parse(name));

        builder.HasStandardAuditColumns();
    }
}

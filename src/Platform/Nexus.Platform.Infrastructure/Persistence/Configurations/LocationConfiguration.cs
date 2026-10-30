using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Locations;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.location</c> (no latitude/longitude until step 1.1).</summary>
internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("location");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CountryCode).HasColumnType("char(2)");

        builder.HasStandardAuditColumns();
    }
}

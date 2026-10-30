using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Currencies;
using Nexus.Platform.Domain.Groups;
using Nexus.Platform.Domain.Parties;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.&quot;group&quot;</c> (quoted: <c>group</c> is a reserved word; EF Core always quotes).</summary>
internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("group");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ReportingCurrencyCode).HasColumnType("char(3)");

        // No navigations (separate aggregates); declared so SaveChanges inserts the Party
        // this Group's create use case creates inline (ORG-R1) before the Group itself.
        builder.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Currency>().WithMany().HasForeignKey(e => e.ReportingCurrencyCode).OnDelete(DeleteBehavior.Restrict);

        builder.HasStandardAuditColumns();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.BusinessUnits;
using Nexus.Platform.Domain.Parties;
using Nexus.Platform.Domain.Users;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.app_user</c> (<c>"user"</c> is reserved, hence <c>app_user</c>).</summary>
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("app_user");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PreferredLanguage).HasColumnType("char(2)");
        builder.Ignore(e => e.IsSystem);

        // No navigation properties on either side (Party/BusinessUnit are separate aggregates,
        // conventions-backend.md §3), but EF still needs these relationships declared so
        // SaveChanges orders inserts correctly when a use case creates the Party and the
        // AppUser together (ORG-R4) - without them EF has no FK metadata to sequence on and
        // may insert app_user before its party, violating app_user_party_id_fkey (found live).
        builder.HasOne<Party>().WithMany().HasForeignKey(e => e.PartyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessUnit>().WithMany().HasForeignKey(e => e.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);

        builder.HasStandardAuditColumns();
    }
}

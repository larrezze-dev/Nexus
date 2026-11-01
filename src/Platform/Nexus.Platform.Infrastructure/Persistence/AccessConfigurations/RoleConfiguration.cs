using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Roles;
using Nexus.Platform.Infrastructure.Persistence.Configurations;

namespace Nexus.Platform.Infrastructure.Persistence.AccessConfigurations;

/// <summary><c>access.role</c>.</summary>
internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("role");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CaptionI18n).IsI18nText();

        builder.HasStandardAuditColumns();
    }
}

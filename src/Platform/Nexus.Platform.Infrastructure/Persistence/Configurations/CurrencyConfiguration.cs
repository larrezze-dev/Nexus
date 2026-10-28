using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Domain.Currencies;

namespace Nexus.Platform.Infrastructure.Persistence.Configurations;

/// <summary><c>org.currency</c> (key: <c>code</c>).</summary>
internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("currency");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("code").HasColumnType("char(3)").ValueGeneratedNever();
        builder.Property(e => e.CaptionI18n).IsI18nText();

        builder.HasStandardAuditColumns();
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLedger.Domain.Merchants;

namespace PayLedger.Infrastructure.Persistence.Configurations;

internal sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.Property(m => m.Name).HasMaxLength(100);
        builder.Property(m => m.Email).HasMaxLength(254);
        builder.Property(m => m.ApiKeyHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(m => m.WebhookUrl).HasMaxLength(500);
        builder.Property(m => m.WebhookSecret).HasMaxLength(100).IsUnicode(false);
        builder.HasIndex(m => m.Email).IsUnique();
        builder.HasIndex(m => m.ApiKeyHash).IsUnique();
    }
}

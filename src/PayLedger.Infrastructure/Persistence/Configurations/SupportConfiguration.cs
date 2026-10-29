using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLedger.Domain.Idempotency;
using PayLedger.Domain.Merchants;
using PayLedger.Domain.Webhooks;

namespace PayLedger.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.Property(r => r.Key).HasMaxLength(100);
        builder.Property(r => r.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.ResponseJson).HasMaxLength(-1);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(r => r.MerchantId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.MerchantId, r.Key }).IsUnique();
        builder.HasIndex(r => r.CreatedAt);
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.Property(d => d.EventType).HasMaxLength(50);
        builder.Property(d => d.Payload).HasMaxLength(-1);
        builder.Property(d => d.LastError).HasMaxLength(500);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(d => d.MerchantId).OnDelete(DeleteBehavior.Cascade);

        // The dispatcher only ever looks at pending deliveries that are due.
        builder.HasIndex(d => d.NextAttemptAt).HasFilter("[DeliveredAt] IS NULL");
    }
}

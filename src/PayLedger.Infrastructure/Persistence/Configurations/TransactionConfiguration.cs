using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Merchants;
using PayLedger.Domain.Transactions;

namespace PayLedger.Infrastructure.Persistence.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.Property(t => t.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(t => t.DestinationCurrency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(t => t.ExchangeRate).HasPrecision(18, 8);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(t => t.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.SourceAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(t => t.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Entries).WithOne().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(t => t.Entries).HasField("_entries").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => new { t.MerchantId, t.Id });
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.Property(e => e.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.HasOne<Account>().WithMany().HasForeignKey(e => e.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.AccountId, e.Id });
    }
}

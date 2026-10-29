using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Merchants;

namespace PayLedger.Infrastructure.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.Property(a => a.Name).HasMaxLength(100);
        builder.Property(a => a.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.HasOne<Merchant>().WithMany().HasForeignKey(a => a.MerchantId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.MerchantId, a.Id });

        // Exactly one settlement account per merchant and currency.
        builder.HasIndex(a => new { a.MerchantId, a.Currency })
            .IsUnique()
            .HasFilter($"[Kind] = '{nameof(AccountKind.Settlement)}'");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Accounts_WalletNotNegative",
            $"[Kind] <> '{nameof(AccountKind.Wallet)}' OR [Balance] >= 0"));
    }
}

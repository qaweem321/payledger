using PayLedger.Domain.Common;

namespace PayLedger.Domain.Accounts;

public class Account
{
    private Account() { }

    private Account(long merchantId, string name, string currency, AccountKind kind, DateTimeOffset now)
    {
        if (!Currencies.IsSupported(currency))
        {
            throw new DomainException("unsupported_currency", $"Currency '{currency}' is not supported.");
        }

        MerchantId = merchantId;
        Name = name;
        Currency = currency;
        Kind = kind;
        CreatedAt = now;
    }

    public long Id { get; private set; }
    public long MerchantId { get; private set; }
    public string Name { get; private set; } = "";
    public string Currency { get; private set; } = "";
    public AccountKind Kind { get; private set; }

    /// <summary>Balance in minor units (cents for USD).</summary>
    public long Balance { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Account OpenWallet(long merchantId, string name, string currency, DateTimeOffset now) =>
        new(merchantId, name, currency, AccountKind.Wallet, now);

    public static Account OpenSettlement(long merchantId, string currency, DateTimeOffset now) =>
        new(merchantId, $"{currency} settlement", currency, AccountKind.Settlement, now);

    /// <summary>Adds a signed amount. Only <see cref="Transaction"/> posts to accounts.</summary>
    internal long Apply(long amount)
    {
        var newBalance = checked(Balance + amount);
        if (newBalance < 0 && Kind == AccountKind.Wallet)
        {
            throw new DomainException(
                "insufficient_funds",
                $"Account {Id} has {Balance} and cannot pay {-amount} {Currency}.");
        }

        Balance = newBalance;
        return newBalance;
    }
}

using PayLedger.Domain.Accounts;
using PayLedger.Domain.Common;

namespace PayLedger.Domain.Transactions;

/// <summary>
/// A money movement recorded with double-entry bookkeeping: for every currency involved,
/// the entries of a transaction add up to zero, so money is never created or lost.
/// </summary>
public class Transaction
{
    private readonly List<LedgerEntry> _entries = [];

    private Transaction() { }

    private Transaction(long merchantId, TransactionType type, string? reference, DateTimeOffset now)
    {
        MerchantId = merchantId;
        Type = type;
        Reference = reference;
        CreatedAt = now;
    }

    public long Id { get; private set; }
    public long MerchantId { get; private set; }
    public TransactionType Type { get; private set; }
    public long? SourceAccountId { get; private set; }
    public long? DestinationAccountId { get; private set; }
    public long Amount { get; private set; }
    public string Currency { get; private set; } = "";
    public long DestinationAmount { get; private set; }
    public string DestinationCurrency { get; private set; } = "";
    public decimal? ExchangeRate { get; private set; }
    public string? Reference { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<LedgerEntry> Entries => _entries;

    /// <summary>Money arrives from outside (bank transfer, card payment) into a wallet.</summary>
    public static Transaction Deposit(Account settlement, Account wallet, long amount, string? reference, DateTimeOffset now)
    {
        RequirePositive(amount);
        RequireSameCurrency(settlement, wallet);
        RequireSettlement(settlement, wallet);
        var tx = new Transaction(wallet.MerchantId, TransactionType.Deposit, reference, now)
        {
            DestinationAccountId = wallet.Id,
        };
        tx.SetAmounts(amount, wallet.Currency, amount, wallet.Currency, rate: null);
        tx.Post(settlement, -amount, now);
        tx.Post(wallet, amount, now);
        return tx.Balanced();
    }

    /// <summary>Money leaves a wallet to the outside world (payout).</summary>
    public static Transaction Withdraw(Account wallet, Account settlement, long amount, string? reference, DateTimeOffset now)
    {
        RequirePositive(amount);
        RequireSameCurrency(settlement, wallet);
        RequireSettlement(settlement, wallet);
        var tx = new Transaction(wallet.MerchantId, TransactionType.Withdrawal, reference, now)
        {
            SourceAccountId = wallet.Id,
        };
        tx.SetAmounts(amount, wallet.Currency, amount, wallet.Currency, rate: null);
        tx.Post(wallet, -amount, now);
        tx.Post(settlement, amount, now);
        return tx.Balanced();
    }

    /// <summary>
    /// Moves money between two wallets. When currencies differ, the money passes through the
    /// settlement account of each currency, so each currency still balances on its own.
    /// </summary>
    public static Transaction Transfer(
        Account source,
        Account destination,
        long amount,
        FxLeg? fx,
        string? reference,
        DateTimeOffset now)
    {
        RequirePositive(amount);
        if (source.Id == destination.Id)
        {
            throw new DomainException("same_account", "Source and destination must be different accounts.");
        }

        if (source.Kind != AccountKind.Wallet || destination.Kind != AccountKind.Wallet)
        {
            throw new DomainException("invalid_account", "Transfers move money between wallets only.");
        }

        if (source.MerchantId != destination.MerchantId)
        {
            throw new DomainException("invalid_account", "Both accounts must belong to the same merchant.");
        }

        var tx = new Transaction(source.MerchantId, TransactionType.Transfer, reference, now)
        {
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
        };

        if (source.Currency == destination.Currency)
        {
            if (fx is not null)
            {
                throw new DomainException("invalid_rate", "Same-currency transfers do not use an exchange rate.");
            }

            tx.SetAmounts(amount, source.Currency, amount, destination.Currency, rate: null);
            tx.Post(source, -amount, now);
            tx.Post(destination, amount, now);
            return tx.Balanced();
        }

        if (fx is null)
        {
            throw new DomainException("rate_required", "Cross-currency transfers need an exchange rate.");
        }

        RequireSettlement(fx.SourceSettlement, source);
        RequireSettlement(fx.DestinationSettlement, destination);
        var converted = Currencies.Convert(amount, source.Currency, destination.Currency, fx.Rate);
        if (converted <= 0)
        {
            throw new DomainException("amount_too_small", "The amount is too small to convert.");
        }

        tx.SetAmounts(amount, source.Currency, converted, destination.Currency, fx.Rate);
        tx.Post(source, -amount, now);
        tx.Post(fx.SourceSettlement, amount, now);
        tx.Post(fx.DestinationSettlement, -converted, now);
        tx.Post(destination, converted, now);
        return tx.Balanced();
    }

    private void SetAmounts(long amount, string currency, long destinationAmount, string destinationCurrency, decimal? rate)
    {
        Amount = amount;
        Currency = currency;
        DestinationAmount = destinationAmount;
        DestinationCurrency = destinationCurrency;
        ExchangeRate = rate;
    }

    private void Post(Account account, long amount, DateTimeOffset now)
    {
        var balanceAfter = account.Apply(amount);
        _entries.Add(new LedgerEntry(account.Id, account.Currency, amount, balanceAfter, now));
    }

    private Transaction Balanced()
    {
        var unbalanced = _entries.GroupBy(e => e.Currency).FirstOrDefault(g => g.Sum(e => e.Amount) != 0);
        return unbalanced is null
            ? this
            : throw new InvalidOperationException($"Transaction does not balance in {unbalanced.Key}.");
    }

    private static void RequirePositive(long amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("invalid_amount", "Amount must be greater than zero.");
        }
    }

    private static void RequireSameCurrency(Account a, Account b)
    {
        if (a.Currency != b.Currency)
        {
            throw new DomainException("currency_mismatch", "Both accounts must use the same currency.");
        }
    }

    private static void RequireSettlement(Account settlement, Account wallet)
    {
        if (settlement.Kind != AccountKind.Settlement
            || settlement.MerchantId != wallet.MerchantId
            || settlement.Currency != wallet.Currency)
        {
            throw new InvalidOperationException("Wrong settlement account for this wallet.");
        }

        if (wallet.Kind != AccountKind.Wallet)
        {
            throw new DomainException("invalid_account", "Money can only be moved to or from wallets.");
        }
    }
}

/// <summary>The exchange rate and settlement accounts used by a cross-currency transfer.</summary>
public sealed record FxLeg(decimal Rate, Account SourceSettlement, Account DestinationSettlement);

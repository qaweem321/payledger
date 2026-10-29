namespace PayLedger.Domain.Transactions;

/// <summary>One line of the ledger: a signed amount posted to one account. Entries are never changed.</summary>
public class LedgerEntry
{
    private LedgerEntry() { }

    internal LedgerEntry(long accountId, string currency, long amount, long balanceAfter, DateTimeOffset now)
    {
        AccountId = accountId;
        Currency = currency;
        Amount = amount;
        BalanceAfter = balanceAfter;
        CreatedAt = now;
    }

    public long Id { get; private set; }
    public long TransactionId { get; private set; }
    public long AccountId { get; private set; }
    public string Currency { get; private set; } = "";
    public long Amount { get; private set; }
    public long BalanceAfter { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

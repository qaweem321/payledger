using PayLedger.Domain.Accounts;

namespace PayLedger.UnitTests;

internal static class TestAccounts
{
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
    private static long _nextId = 1;

    public static Account Wallet(string currency = "USD", long merchantId = 1) =>
        WithId(Account.OpenWallet(merchantId, "Wallet", currency, Now));

    public static Account Settlement(string currency = "USD", long merchantId = 1) =>
        WithId(Account.OpenSettlement(merchantId, currency, Now));

    /// <summary>Ids come from the database in the app; tests give each account a distinct one.</summary>
    private static Account WithId(Account account)
    {
        typeof(Account).GetProperty(nameof(Account.Id))!.SetValue(account, Interlocked.Increment(ref _nextId));
        return account;
    }
}

using System.ComponentModel.DataAnnotations;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Transactions;

namespace PayLedger.Application.Accounts;

public sealed record CreateAccountRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Required, StringLength(3, MinimumLength = 3)] string Currency);

public sealed record AccountDto(long Id, string Name, string Currency, AccountKind Kind, long Balance, DateTimeOffset CreatedAt)
{
    public static AccountDto From(Account a) => new(a.Id, a.Name, a.Currency, a.Kind, a.Balance, a.CreatedAt);
}

public sealed record LedgerEntryDto(long Id, long TransactionId, long AccountId, string Currency, long Amount, long BalanceAfter, DateTimeOffset CreatedAt)
{
    public static LedgerEntryDto From(LedgerEntry e) =>
        new(e.Id, e.TransactionId, e.AccountId, e.Currency, e.Amount, e.BalanceAfter, e.CreatedAt);
}

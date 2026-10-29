using System.ComponentModel.DataAnnotations;
using PayLedger.Application.Accounts;
using PayLedger.Domain.Transactions;

namespace PayLedger.Application.Payments;

public static class AmountLimits
{
    /// <summary>Upper limit per movement in minor units, far below overflow even after currency conversion.</summary>
    public const long Max = 1_000_000_000_000;
}

public sealed record DepositRequest(
    [Range(1, long.MaxValue)] long AccountId,
    [Range(1, AmountLimits.Max)] long Amount,
    [StringLength(200)] string? Reference);

public sealed record WithdrawalRequest(
    [Range(1, long.MaxValue)] long AccountId,
    [Range(1, AmountLimits.Max)] long Amount,
    [StringLength(200)] string? Reference);

public sealed record TransferRequest(
    [Range(1, long.MaxValue)] long SourceAccountId,
    [Range(1, long.MaxValue)] long DestinationAccountId,
    [Range(1, AmountLimits.Max)] long Amount,
    [StringLength(200)] string? Reference);

public sealed record TransactionDto(
    long Id,
    TransactionType Type,
    long? SourceAccountId,
    long? DestinationAccountId,
    long Amount,
    string Currency,
    long DestinationAmount,
    string DestinationCurrency,
    decimal? ExchangeRate,
    string? Reference,
    DateTimeOffset CreatedAt,
    IReadOnlyList<LedgerEntryDto> Entries)
{
    public static TransactionDto From(Transaction t) => new(
        t.Id, t.Type, t.SourceAccountId, t.DestinationAccountId, t.Amount, t.Currency,
        t.DestinationAmount, t.DestinationCurrency, t.ExchangeRate, t.Reference, t.CreatedAt,
        t.Entries.OrderBy(e => e.Id).Select(LedgerEntryDto.From).ToList());
}

using Microsoft.EntityFrameworkCore;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Application.Webhooks;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Transactions;

namespace PayLedger.Application.Payments;

public sealed class PaymentService(
    IAppDbContext db,
    TimeProvider clock,
    IExchangeRateProvider rates,
    IdempotencyExecutor idempotency,
    WebhookOutbox outbox)
{
    public async Task<IdempotentResult<TransactionDto>> DepositAsync(long merchantId, string? key, DepositRequest request, CancellationToken ct)
    {
        var fingerprint = IdempotencyExecutor.Fingerprint("deposit", request);
        return await idempotency.TryReplayAsync<TransactionDto>(merchantId, key, fingerprint, ct)
            ?? await MoveAsync(merchantId, key, fingerprint, [request.AccountId], (accounts, now) =>
            {
                var wallet = Find(accounts, request.AccountId);
                return Transaction.Deposit(Settlement(accounts, wallet.Currency), wallet, request.Amount, request.Reference, now);
            }, ct);
    }

    public async Task<IdempotentResult<TransactionDto>> WithdrawAsync(long merchantId, string? key, WithdrawalRequest request, CancellationToken ct)
    {
        var fingerprint = IdempotencyExecutor.Fingerprint("withdrawal", request);
        return await idempotency.TryReplayAsync<TransactionDto>(merchantId, key, fingerprint, ct)
            ?? await MoveAsync(merchantId, key, fingerprint, [request.AccountId], (accounts, now) =>
            {
                var wallet = Find(accounts, request.AccountId);
                return Transaction.Withdraw(wallet, Settlement(accounts, wallet.Currency), request.Amount, request.Reference, now);
            }, ct);
    }

    public async Task<IdempotentResult<TransactionDto>> TransferAsync(long merchantId, string? key, TransferRequest request, CancellationToken ct)
    {
        var fingerprint = IdempotencyExecutor.Fingerprint("transfer", request);
        if (await idempotency.TryReplayAsync<TransactionDto>(merchantId, key, fingerprint, ct) is { } replay)
        {
            return replay;
        }

        // Fetch the exchange rate before any row is locked: a slow rate service must never hold locks.
        var currencies = await db.Accounts.AsNoTracking()
            .Where(a => a.MerchantId == merchantId && (a.Id == request.SourceAccountId || a.Id == request.DestinationAccountId))
            .ToDictionaryAsync(a => a.Id, a => a.Currency, ct);
        if (!currencies.TryGetValue(request.SourceAccountId, out var from)
            || !currencies.TryGetValue(request.DestinationAccountId, out var to))
        {
            throw new NotFoundException("Account");
        }

        decimal? rate = from == to ? null : await rates.GetRateAsync(from, to, ct);

        return await MoveAsync(merchantId, key, fingerprint, [request.SourceAccountId, request.DestinationAccountId], (accounts, now) =>
        {
            var source = Find(accounts, request.SourceAccountId);
            var destination = Find(accounts, request.DestinationAccountId);
            var fx = rate is { } r
                ? new FxLeg(r, Settlement(accounts, source.Currency), Settlement(accounts, destination.Currency))
                : null;
            return Transaction.Transfer(source, destination, request.Amount, fx, request.Reference, now);
        }, ct);
    }

    /// <summary>Locks the accounts, builds the transaction, saves it and queues its webhook, all in one database transaction.</summary>
    private Task<IdempotentResult<TransactionDto>> MoveAsync(
        long merchantId,
        string? key,
        string fingerprint,
        long[] walletIds,
        Func<List<Account>, DateTimeOffset, Transaction> build,
        CancellationToken ct) =>
        idempotency.ExecuteAsync(merchantId, key, fingerprint, async token =>
        {
            var lockIds = await WithSettlementIdsAsync(merchantId, walletIds, token);
            var accounts = await db.LockAccountsAsync(merchantId, lockIds, token);

            var transaction = build(accounts, clock.GetUtcNow());
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync(token);

            var dto = TransactionDto.From(transaction);
            await outbox.EnqueueAsync(merchantId, "transaction.completed", dto, token);
            return dto;
        }, ct);

    /// <summary>The wallets plus the settlement accounts of their currencies, so all of them are locked together.</summary>
    private async Task<long[]> WithSettlementIdsAsync(long merchantId, long[] walletIds, CancellationToken ct)
    {
        var walletCurrencies = db.Accounts
            .Where(w => w.MerchantId == merchantId && walletIds.Contains(w.Id))
            .Select(w => w.Currency);
        var settlementIds = await db.Accounts
            .Where(a => a.MerchantId == merchantId && a.Kind == AccountKind.Settlement && walletCurrencies.Contains(a.Currency))
            .Select(a => a.Id)
            .ToListAsync(ct);
        return [.. walletIds.Concat(settlementIds).Distinct()];
    }

    private static Account Find(List<Account> accounts, long id) =>
        accounts.SingleOrDefault(a => a.Id == id) ?? throw new NotFoundException("Account");

    private static Account Settlement(List<Account> accounts, string currency) =>
        accounts.SingleOrDefault(a => a.Kind == AccountKind.Settlement && a.Currency == currency)
        ?? throw new InvalidOperationException($"Settlement account for {currency} is missing.");
}

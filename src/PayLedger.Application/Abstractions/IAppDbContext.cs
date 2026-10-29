using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Idempotency;
using PayLedger.Domain.Merchants;
using PayLedger.Domain.Transactions;
using PayLedger.Domain.Webhooks;

namespace PayLedger.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<Merchant> Merchants { get; }
    DbSet<Account> Accounts { get; }
    DbSet<Transaction> Transactions { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<WebhookDelivery> WebhookDeliveries { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads the merchant's accounts and locks their rows until the current database transaction
    /// ends, in a fixed order so two transfers between the same accounts cannot deadlock.
    /// </summary>
    Task<List<Account>> LockAccountsAsync(long merchantId, IReadOnlyCollection<long> accountIds, CancellationToken cancellationToken);

    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>Forgets tracked changes after a failed save, so the context can be used again.</summary>
    void ClearChanges();
}

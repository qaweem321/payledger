using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PayLedger.Application.Abstractions;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Idempotency;
using PayLedger.Domain.Merchants;
using PayLedger.Domain.Transactions;
using PayLedger.Domain.Webhooks;

namespace PayLedger.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task<List<Account>> LockAccountsAsync(long merchantId, IReadOnlyCollection<long> accountIds, CancellationToken cancellationToken)
    {
        // UPDLOCK keeps the rows locked until commit; ids are passed sorted so locks are taken in one order.
        var ids = JsonSerializer.Serialize(accountIds.Order());
        return await Accounts
            .FromSql($"""
                SELECT a.* FROM Accounts a WITH (UPDLOCK, ROWLOCK)
                WHERE a.MerchantId = {merchantId}
                  AND a.Id IN (SELECT CAST([value] AS bigint) FROM OPENJSON({ids}))
                """)
            .ToListAsync(cancellationToken);
    }

    public bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    public void ClearChanges() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<string>().HaveMaxLength(200);
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);
    }
}

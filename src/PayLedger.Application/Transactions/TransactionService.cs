using Microsoft.EntityFrameworkCore;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Application.Payments;
using PayLedger.Domain.Transactions;

namespace PayLedger.Application.Transactions;

public sealed record TransactionFilter(long? AccountId = null, TransactionType? Type = null);

public sealed class TransactionService(IAppDbContext db)
{
    public Task<Page<TransactionDto>> ListAsync(long merchantId, TransactionFilter filter, PageRequest page, CancellationToken ct) =>
        db.Transactions.AsNoTracking()
            .Include(t => t.Entries)
            .Where(t => t.MerchantId == merchantId)
            .Where(t => filter.AccountId == null || t.SourceAccountId == filter.AccountId || t.DestinationAccountId == filter.AccountId)
            .Where(t => filter.Type == null || t.Type == filter.Type)
            .Where(t => page.StartingAfter == null || t.Id < page.StartingAfter)
            .OrderByDescending(t => t.Id)
            .ToPageAsync(TransactionDto.From, page.SafeLimit, ct);

    public async Task<TransactionDto> GetAsync(long merchantId, long transactionId, CancellationToken ct) =>
        TransactionDto.From(await db.Transactions.AsNoTracking()
            .Include(t => t.Entries)
            .SingleOrDefaultAsync(t => t.Id == transactionId && t.MerchantId == merchantId, ct)
            ?? throw new NotFoundException("Transaction"));
}

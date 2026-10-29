using Microsoft.EntityFrameworkCore;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Common;

namespace PayLedger.Application.Accounts;

public sealed class AccountService(IAppDbContext db, TimeProvider clock)
{
    public async Task<AccountDto> CreateAsync(long merchantId, CreateAccountRequest request, CancellationToken ct)
    {
        var currency = request.Currency.ToUpperInvariant();
        if (!Currencies.IsSupported(currency))
        {
            throw new DomainException(
                "unsupported_currency",
                $"Currency '{request.Currency}' is not supported. Use one of: {string.Join(", ", Currencies.All)}.");
        }

        var account = Account.OpenWallet(merchantId, request.Name.Trim(), currency, clock.GetUtcNow());
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return AccountDto.From(account);
    }

    public Task<Page<AccountDto>> ListAsync(long merchantId, PageRequest page, CancellationToken ct) =>
        db.Accounts.AsNoTracking()
            .Where(a => a.MerchantId == merchantId)
            .Where(a => page.StartingAfter == null || a.Id < page.StartingAfter)
            .OrderByDescending(a => a.Id)
            .ToPageAsync(AccountDto.From, page.SafeLimit, ct);

    public async Task<AccountDto> GetAsync(long merchantId, long accountId, CancellationToken ct) =>
        AccountDto.From(await db.Accounts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == accountId && a.MerchantId == merchantId, ct)
            ?? throw new NotFoundException("Account"));

    public async Task<Page<LedgerEntryDto>> ListEntriesAsync(long merchantId, long accountId, PageRequest page, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId && a.MerchantId == merchantId, ct))
        {
            throw new NotFoundException("Account");
        }

        return await db.LedgerEntries.AsNoTracking()
            .Where(e => e.AccountId == accountId)
            .Where(e => page.StartingAfter == null || e.Id < page.StartingAfter)
            .OrderByDescending(e => e.Id)
            .ToPageAsync(LedgerEntryDto.From, page.SafeLimit, ct);
    }
}

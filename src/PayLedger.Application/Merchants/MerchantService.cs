using Microsoft.EntityFrameworkCore;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Application.Webhooks;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Common;
using PayLedger.Domain.Merchants;

namespace PayLedger.Application.Merchants;

public sealed class MerchantService(IAppDbContext db, TimeProvider clock, IWebhookUrlValidator urlValidator)
{
    public const string ApiKeyPrefix = "pl_test_";

    public async Task<MerchantWithKeyDto> SignupAsync(SignupRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Merchants.AnyAsync(m => m.Email == email, ct))
        {
            throw new ConflictException("email_taken", "A merchant with this email already exists.");
        }

        var apiKey = Secrets.NewToken(ApiKeyPrefix);
        var now = clock.GetUtcNow();
        var merchant = new Merchant(request.Name.Trim(), email, Secrets.Hash(apiKey), now);

        await using var tx = await db.BeginTransactionAsync(ct);
        db.Merchants.Add(merchant);
        await SaveUniqueAsync(ct);

        // One settlement account per currency, created up front so money movements never race to create them.
        db.Accounts.AddRange(Currencies.All.Select(c => Account.OpenSettlement(merchant.Id, c, now)));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new MerchantWithKeyDto(MerchantDto.From(merchant), apiKey);
    }

    public async Task<MerchantDto> GetAsync(long merchantId, CancellationToken ct) =>
        MerchantDto.From(await FindAsync(merchantId, ct));

    public async Task<MerchantWithKeyDto> RotateApiKeyAsync(long merchantId, CancellationToken ct)
    {
        var merchant = await FindAsync(merchantId, ct);
        var apiKey = Secrets.NewToken(ApiKeyPrefix);
        merchant.RotateApiKey(Secrets.Hash(apiKey));
        await db.SaveChangesAsync(ct);
        return new MerchantWithKeyDto(MerchantDto.From(merchant), apiKey);
    }

    public async Task<WebhookSecretDto> SetWebhookAsync(long merchantId, SetWebhookRequest request, CancellationToken ct)
    {
        var error = urlValidator.Validate(request.Url);
        if (error is not null)
        {
            throw new DomainException("invalid_webhook_url", error);
        }

        var merchant = await FindAsync(merchantId, ct);
        var secret = Secrets.NewToken("whsec_");
        merchant.SetWebhook(request.Url, secret);
        await db.SaveChangesAsync(ct);
        return new WebhookSecretDto(request.Url, secret);
    }

    public async Task RemoveWebhookAsync(long merchantId, CancellationToken ct)
    {
        var merchant = await FindAsync(merchantId, ct);
        merchant.RemoveWebhook();
        await db.SaveChangesAsync(ct);
    }

    private async Task<Merchant> FindAsync(long merchantId, CancellationToken ct) =>
        await db.Merchants.SingleOrDefaultAsync(m => m.Id == merchantId, ct)
        ?? throw new NotFoundException("Merchant");

    private async Task SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex))
        {
            // Two signups with the same email at the same moment: the unique index decides.
            throw new ConflictException("email_taken", "A merchant with this email already exists.");
        }
    }
}

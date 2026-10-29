using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Domain.Webhooks;

namespace PayLedger.Application.Webhooks;

public sealed record WebhookEvent<T>(string Id, string Type, DateTimeOffset CreatedAt, T Data);

public sealed class WebhookOutbox(IAppDbContext db, TimeProvider clock)
{
    /// <summary>Queues an event for the merchant's webhook, if one is set. Call inside the business transaction.</summary>
    public async Task EnqueueAsync<T>(long merchantId, string type, T data, CancellationToken ct)
    {
        var hasWebhook = await db.Merchants.AnyAsync(m => m.Id == merchantId && m.WebhookUrl != null, ct);
        if (!hasWebhook)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var payload = JsonSerializer.Serialize(
            new WebhookEvent<T>("evt_" + Guid.CreateVersion7().ToString("N"), type, now, data),
            JsonDefaults.Options);
        db.WebhookDeliveries.Add(new WebhookDelivery(merchantId, type, payload, now));
        await db.SaveChangesAsync(ct);
    }
}

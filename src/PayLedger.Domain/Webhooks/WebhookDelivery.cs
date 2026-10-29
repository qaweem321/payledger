namespace PayLedger.Domain.Webhooks;

/// <summary>
/// An event waiting to be sent to a merchant's webhook URL (transactional outbox). It is saved in
/// the same database transaction as the change it describes, so no event is lost or invented.
/// </summary>
public class WebhookDelivery
{
    public const int MaxAttempts = 8;

    private WebhookDelivery() { }

    public WebhookDelivery(long merchantId, string eventType, string payload, DateTimeOffset now)
    {
        MerchantId = merchantId;
        EventType = eventType;
        Payload = payload;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public long Id { get; private set; }
    public long MerchantId { get; private set; }
    public string EventType { get; private set; } = "";
    public string Payload { get; private set; } = "";
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Exponential backoff: 30s, 1m, 2m, 4m ... capped at 6 hours.</summary>
    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempts - 1), TimeSpan.FromHours(6).TotalSeconds));
}

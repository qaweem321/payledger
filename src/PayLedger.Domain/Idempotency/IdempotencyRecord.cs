namespace PayLedger.Domain.Idempotency;

/// <summary>Remembers the result of a request sent with an Idempotency-Key, so a retry returns it again.</summary>
public class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(long merchantId, string key, string requestHash, DateTimeOffset now)
    {
        MerchantId = merchantId;
        Key = key;
        RequestHash = requestHash;
        CreatedAt = now;
    }

    public long Id { get; private set; }
    public long MerchantId { get; private set; }
    public string Key { get; private set; } = "";
    public string RequestHash { get; private set; } = "";
    public string? ResponseJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(string responseJson) => ResponseJson = responseJson;
}

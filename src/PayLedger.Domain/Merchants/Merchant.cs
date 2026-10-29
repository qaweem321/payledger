namespace PayLedger.Domain.Merchants;

public class Merchant
{
    private Merchant() { }

    public Merchant(string name, string email, string apiKeyHash, DateTimeOffset now)
    {
        Name = name;
        Email = email.Trim().ToLowerInvariant();
        ApiKeyHash = apiKeyHash;
        CreatedAt = now;
    }

    public long Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string ApiKeyHash { get; private set; } = "";
    public string? WebhookUrl { get; private set; }
    public string? WebhookSecret { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetWebhook(string url, string secret)
    {
        WebhookUrl = url;
        WebhookSecret = secret;
    }

    public void RemoveWebhook()
    {
        WebhookUrl = null;
        WebhookSecret = null;
    }

    public void RotateApiKey(string apiKeyHash) => ApiKeyHash = apiKeyHash;
}

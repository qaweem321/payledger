namespace PayLedger.Application.Webhooks;

public interface IWebhookUrlValidator
{
    /// <summary>Returns an error message, or null when the URL may receive webhooks.</summary>
    string? Validate(string url);
}

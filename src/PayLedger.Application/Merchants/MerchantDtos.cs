using System.ComponentModel.DataAnnotations;
using PayLedger.Domain.Merchants;

namespace PayLedger.Application.Merchants;

public sealed record SignupRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, EmailAddress, StringLength(254)] string Email);

public sealed record SetWebhookRequest([Required, Url, StringLength(500)] string Url);

public sealed record MerchantDto(long Id, string Name, string Email, string? WebhookUrl, DateTimeOffset CreatedAt)
{
    public static MerchantDto From(Merchant m) => new(m.Id, m.Name, m.Email, m.WebhookUrl, m.CreatedAt);
}

/// <summary>Returned once at signup or key rotation. The API key is never shown again.</summary>
public sealed record MerchantWithKeyDto(MerchantDto Merchant, string ApiKey);

/// <summary>Returned once when a webhook is set. Use the secret to verify the PayLedger-Signature header.</summary>
public sealed record WebhookSecretDto(string Url, string Secret);

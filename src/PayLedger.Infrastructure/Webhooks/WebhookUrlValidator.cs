using System.Net;
using Microsoft.Extensions.Options;
using PayLedger.Application.Webhooks;

namespace PayLedger.Infrastructure.Webhooks;

/// <summary>
/// Checks the URL when it is saved. The real protection is in <see cref="WebhookHttpHandler"/>, which
/// checks the resolved IP at connect time, because DNS can point a public name at a private address.
/// </summary>
public sealed class WebhookUrlValidator(IOptions<WebhookOptions> options) : IWebhookUrlValidator
{
    public string? Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "The webhook URL must be an absolute URL.";
        }

        var settings = options.Value;
        if (uri.Scheme != Uri.UriSchemeHttps && !(settings.AllowHttp && uri.Scheme == Uri.UriSchemeHttp))
        {
            return "The webhook URL must use https.";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "The webhook URL must not contain a user name or password.";
        }

        if (settings.AllowPrivateNetworks)
        {
            return null;
        }

        if (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.IdnHost, out var ip) && !NetworkRules.IsPublic(ip)))
        {
            return "The webhook URL must point to a public address.";
        }

        return null;
    }
}

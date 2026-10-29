using System.ComponentModel.DataAnnotations;

namespace PayLedger.Infrastructure.Webhooks;

public sealed class WebhookOptions
{
    public const string Section = "Webhooks";

    /// <summary>Runs the background sender. Turn off on extra API instances if you want one sender only.</summary>
    public bool DispatcherEnabled { get; set; } = true;

    /// <summary>Allow plain http:// URLs. Keep false in production.</summary>
    public bool AllowHttp { get; set; }

    /// <summary>
    /// Allow URLs that resolve to private, loopback or link-local addresses. Keep false in production,
    /// or any merchant could make the server call internal services (SSRF).
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    [Range(typeof(TimeSpan), "00:00:00.050", "00:10:00")]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    [Range(1, 500)]
    public int BatchSize { get; set; } = 20;

    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

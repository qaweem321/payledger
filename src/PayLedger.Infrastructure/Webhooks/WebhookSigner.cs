using System.Security.Cryptography;
using System.Text;

namespace PayLedger.Infrastructure.Webhooks;

/// <summary>
/// Signs webhook bodies as <c>t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of "t.body"&gt;</c>.
/// Receivers recompute the HMAC with their secret and reject old timestamps to stop replays.
/// </summary>
public static class WebhookSigner
{
    public const string HeaderName = "PayLedger-Signature";

    public static string Sign(string secret, long timestamp, string body)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(mac)}";
    }

    public static bool Verify(string secret, string header, string body, DateTimeOffset now, TimeSpan tolerance)
    {
        var parts = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
        if (!parts.TryGetValue("t", out var t) || !long.TryParse(t, out var timestamp) || !parts.ContainsKey("v1"))
        {
            return false;
        }

        if (Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(secret, timestamp, body));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(header));
    }
}

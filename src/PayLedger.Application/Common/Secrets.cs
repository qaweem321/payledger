using System.Security.Cryptography;
using System.Text;

namespace PayLedger.Application.Common;

public static class Secrets
{
    /// <summary>A random token such as <c>pl_test_3fK...</c> (32 random bytes, URL-safe).</summary>
    public static string NewToken(string prefix) =>
        prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// SHA-256 is enough for API keys: they are 256-bit random values, so a slow password hash
    /// adds nothing, and a fast hash keeps each authenticated request to one indexed lookup.
    /// </summary>
    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

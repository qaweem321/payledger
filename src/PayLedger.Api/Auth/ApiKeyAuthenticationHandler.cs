using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Application.Merchants;

namespace PayLedger.Api.Auth;

/// <summary>Authenticates <c>Authorization: Bearer pl_test_...</c> with one indexed lookup of the key's hash.</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = header["Bearer ".Length..].Trim();
        if (!apiKey.StartsWith(MerchantService.ApiKeyPrefix, StringComparison.Ordinal) || apiKey.Length > 100)
        {
            return AuthenticateResult.Fail("Malformed API key.");
        }

        var hash = Secrets.Hash(apiKey);
        var merchantId = await db.Merchants.AsNoTracking()
            .Where(m => m.ApiKeyHash == hash)
            .Select(m => (long?)m.Id)
            .SingleOrDefaultAsync(Context.RequestAborted);
        if (merchantId is null)
        {
            return AuthenticateResult.Fail("Unknown API key.");
        }

        var identity = new ClaimsIdentity([new Claim(MerchantClaims.MerchantId, merchantId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Missing or invalid API key.",
                detail: "Send your key as 'Authorization: Bearer pl_test_...'.",
                extensions: new Dictionary<string, object?> { ["code"] = "unauthorized" })
            .ExecuteAsync(Context);
    }
}

public static class MerchantClaims
{
    public const string MerchantId = "merchant_id";

    public static long GetMerchantId(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(MerchantId) ?? throw new InvalidOperationException("Not authenticated."), System.Globalization.CultureInfo.InvariantCulture);
}

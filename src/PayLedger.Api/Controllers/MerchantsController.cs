using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PayLedger.Api.Infrastructure;
using PayLedger.Application.Merchants;

namespace PayLedger.Api.Controllers;

[Route("v1/merchants")]
public sealed class MerchantsController(MerchantService merchants) : ApiControllerBase
{
    /// <summary>Creates a merchant and returns its API key. The key is shown only once.</summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Signup)]
    [ProducesResponseType<MerchantWithKeyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MerchantWithKeyDto>> Signup(SignupRequest request, CancellationToken ct)
    {
        var result = await merchants.SignupAsync(request, ct);
        return CreatedAtAction(nameof(Me), null, result);
    }

    [HttpGet("me")]
    public Task<MerchantDto> Me(CancellationToken ct) => merchants.GetAsync(MerchantId, ct);

    /// <summary>Replaces the API key. The old key stops working at once.</summary>
    [HttpPost("me/api-key")]
    public Task<MerchantWithKeyDto> RotateApiKey(CancellationToken ct) => merchants.RotateApiKeyAsync(MerchantId, ct);

    /// <summary>Sets the webhook URL and returns a new signing secret (shown only once).</summary>
    [HttpPut("me/webhook")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<WebhookSecretDto> SetWebhook(SetWebhookRequest request, CancellationToken ct) =>
        merchants.SetWebhookAsync(MerchantId, request, ct);

    [HttpDelete("me/webhook")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveWebhook(CancellationToken ct)
    {
        await merchants.RemoveWebhookAsync(MerchantId, ct);
        return NoContent();
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PayLedger.Api.Infrastructure;
using PayLedger.Application.Fx;
using PayLedger.Application.Payments;

namespace PayLedger.Api.Controllers;

[Route("v1/fx")]
public sealed class FxController(FxService fx) : ApiControllerBase
{
    /// <summary>Shows what a transfer would convert to right now, without moving money.</summary>
    [HttpGet("quote")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<FxQuoteDto> Quote(
        [FromQuery, Required, StringLength(3, MinimumLength = 3)] string from,
        [FromQuery, Required, StringLength(3, MinimumLength = 3)] string to,
        [FromQuery, Range(1, AmountLimits.Max)] long amount,
        CancellationToken ct) =>
        fx.QuoteAsync(from, to, amount, ct);
}

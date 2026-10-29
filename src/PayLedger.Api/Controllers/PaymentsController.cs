using Microsoft.AspNetCore.Mvc;
using PayLedger.Api.Infrastructure;
using PayLedger.Application.Payments;

namespace PayLedger.Api.Controllers;

/// <summary>
/// Money movements. Send an Idempotency-Key header (for example a UUID) so a retried request
/// returns the first result instead of moving money twice. Keys are kept for 24 hours.
/// </summary>
[Route("v1")]
[ProducesResponseType<TransactionDto>(StatusCodes.Status201Created)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
public sealed class PaymentsController(PaymentService payments) : ApiControllerBase
{
    /// <summary>Money arrives from outside (bank transfer, card) into a wallet.</summary>
    [HttpPost("deposits")]
    public async Task<ActionResult<TransactionDto>> Deposit(
        DepositRequest request,
        [FromHeader(Name = IdempotencyHeader), IdempotencyKey] string? idempotencyKey,
        CancellationToken ct) =>
        CreatedTransaction(await payments.DepositAsync(MerchantId, idempotencyKey, request, ct));

    /// <summary>Money leaves a wallet (payout). Fails with insufficient_funds if the balance is too low.</summary>
    [HttpPost("withdrawals")]
    public async Task<ActionResult<TransactionDto>> Withdraw(
        WithdrawalRequest request,
        [FromHeader(Name = IdempotencyHeader), IdempotencyKey] string? idempotencyKey,
        CancellationToken ct) =>
        CreatedTransaction(await payments.WithdrawAsync(MerchantId, idempotencyKey, request, ct));

    /// <summary>Moves money between two wallets. Different currencies are converted at the live rate (rounded down).</summary>
    [HttpPost("transfers")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TransactionDto>> Transfer(
        TransferRequest request,
        [FromHeader(Name = IdempotencyHeader), IdempotencyKey] string? idempotencyKey,
        CancellationToken ct) =>
        CreatedTransaction(await payments.TransferAsync(MerchantId, idempotencyKey, request, ct));
}

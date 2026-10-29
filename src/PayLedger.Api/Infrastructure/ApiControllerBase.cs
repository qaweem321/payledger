using Microsoft.AspNetCore.Mvc;
using PayLedger.Api.Auth;
using PayLedger.Application.Payments;

namespace PayLedger.Api.Infrastructure;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    public const string IdempotencyHeader = "Idempotency-Key";

    protected long MerchantId => User.GetMerchantId();

    /// <summary>201 Created for a new or replayed money movement; a replay is marked with Idempotent-Replayed.</summary>
    protected CreatedAtActionResult CreatedTransaction(IdempotentResult<TransactionDto> result)
    {
        if (result.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return CreatedAtAction(
            nameof(Controllers.TransactionsController.Get),
            "Transactions",
            new { id = result.Value.Id },
            result.Value);
    }
}

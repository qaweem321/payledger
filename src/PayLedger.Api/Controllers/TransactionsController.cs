using Microsoft.AspNetCore.Mvc;
using PayLedger.Api.Infrastructure;
using PayLedger.Application.Common;
using PayLedger.Application.Payments;
using PayLedger.Application.Transactions;
using PayLedger.Domain.Transactions;

namespace PayLedger.Api.Controllers;

[Route("v1/transactions")]
public sealed class TransactionsController(TransactionService transactions) : ApiControllerBase
{
    /// <summary>Lists transactions newest first, optionally for one account or of one type.</summary>
    [HttpGet]
    public Task<Page<TransactionDto>> List(
        [FromQuery] long? accountId,
        [FromQuery] TransactionType? type,
        [FromQuery] PageRequest page,
        CancellationToken ct) =>
        transactions.ListAsync(MerchantId, new TransactionFilter(accountId, type), page, ct);

    [HttpGet("{id:long}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TransactionDto> Get(long id, CancellationToken ct) => transactions.GetAsync(MerchantId, id, ct);
}

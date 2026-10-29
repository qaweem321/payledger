using Microsoft.AspNetCore.Mvc;
using PayLedger.Api.Infrastructure;
using PayLedger.Application.Accounts;
using PayLedger.Application.Common;

namespace PayLedger.Api.Controllers;

[Route("v1/accounts")]
public sealed class AccountsController(AccountService accounts) : ApiControllerBase
{
    /// <summary>Opens a wallet in one currency. Balances are in minor units (cents for USD).</summary>
    [HttpPost]
    [ProducesResponseType<AccountDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountRequest request, CancellationToken ct)
    {
        var account = await accounts.CreateAsync(MerchantId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = account.Id }, account);
    }

    /// <summary>Lists wallets and settlement accounts, newest first.</summary>
    [HttpGet]
    public Task<Page<AccountDto>> List([FromQuery] PageRequest page, CancellationToken ct) =>
        accounts.ListAsync(MerchantId, page, ct);

    [HttpGet("{id:long}")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AccountDto> Get(long id, CancellationToken ct) => accounts.GetAsync(MerchantId, id, ct);

    /// <summary>The account's ledger entries, newest first, each with the balance after it.</summary>
    [HttpGet("{id:long}/entries")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<Page<LedgerEntryDto>> Entries(long id, [FromQuery] PageRequest page, CancellationToken ct) =>
        accounts.ListEntriesAsync(MerchantId, id, page, ct);
}

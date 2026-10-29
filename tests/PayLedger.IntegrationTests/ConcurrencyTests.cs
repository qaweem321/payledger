using System.Net;
using System.Net.Http.Json;
using PayLedger.Application.Accounts;
using PayLedger.Application.Common;
using PayLedger.Application.Payments;

namespace PayLedger.IntegrationTests;

/// <summary>Real parallel requests against SQL Server: the cases that break naive payment code.</summary>
public class ConcurrencyTests(ApiFactory factory)
{
    [Fact]
    public async Task Retrying_with_the_same_key_moves_money_once()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();
        var body = new { accountId = wallet.Id, amount = 700 };

        var first = await client.PostAsync("/v1/deposits", body, key);
        var retry = await client.PostAsync("/v1/deposits", body, key);

        Assert.Equal((await MerchantClient.ReadAsync<TransactionDto>(first)).Id, (await MerchantClient.ReadAsync<TransactionDto>(retry)).Id);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(700, (await client.GetAccountAsync(wallet.Id)).Balance);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_refused()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();
        (await client.PostAsync("/v1/deposits", new { accountId = wallet.Id, amount = 700 }, key)).EnsureSuccessStatusCode();

        var different = await client.PostAsync("/v1/deposits", new { accountId = wallet.Id, amount = 900 }, key);

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("idempotency_key_reused", await MerchantClient.ErrorCodeAsync(different));
    }

    [Fact]
    public async Task Same_key_sent_in_parallel_moves_money_once()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => client.PostAsync("/v1/deposits", new { accountId = wallet.Id, amount = 500 }, key)));

        var ids = await Task.WhenAll(responses.Select(MerchantClient.ReadAsync<TransactionDto>));
        Assert.Single(ids.Select(t => t.Id).Distinct());
        Assert.Equal(500, (await client.GetAccountAsync(wallet.Id)).Balance);
    }

    [Fact]
    public async Task Parallel_withdrawals_never_overdraw()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();
        await client.DepositAsync(wallet.Id, 1_000);

        // 20 withdrawals of 100 race for a balance of 1,000: exactly 10 may succeed.
        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.PostAsync("/v1/withdrawals", new { accountId = wallet.Id, amount = 100 })));

        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(0, (await client.GetAccountAsync(wallet.Id)).Balance);
    }

    [Fact]
    public async Task Opposite_transfers_in_parallel_do_not_deadlock_and_the_books_balance()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var a = await client.CreateWalletAsync();
        var b = await client.CreateWalletAsync();
        await client.DepositAsync(a.Id, 5_000);
        await client.DepositAsync(b.Id, 5_000);

        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => i % 2 == 0
            ? client.PostAsync("/v1/transfers", new { sourceAccountId = a.Id, destinationAccountId = b.Id, amount = 300 })
            : client.PostAsync("/v1/transfers", new { sourceAccountId = b.Id, destinationAccountId = a.Id, amount = 200 })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(5_000 - (15 * 300) + (15 * 200), (await client.GetAccountAsync(a.Id)).Balance);
        Assert.Equal(5_000 + (15 * 300) - (15 * 200), (await client.GetAccountAsync(b.Id)).Balance);

        // Double-entry invariant: within one merchant and currency, all balances add up to zero,
        // and every balance equals the sum of its own ledger entries.
        var accounts = (await client.Http.GetFromJsonAsync<Page<AccountDto>>("/v1/accounts?limit=100", MerchantClient.Json))!.Data;
        Assert.All(accounts.GroupBy(x => x.Currency), g => Assert.Equal(0, g.Sum(x => x.Balance)));
        foreach (var account in new[] { a, b })
        {
            var entries = await client.Http.GetFromJsonAsync<Page<LedgerEntryDto>>($"/v1/accounts/{account.Id}/entries?limit=100", MerchantClient.Json);
            Assert.Equal((await client.GetAccountAsync(account.Id)).Balance, entries!.Data.Sum(e => e.Amount));
        }
    }
}

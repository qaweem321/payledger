using System.Net;
using System.Net.Http.Json;
using PayLedger.Application.Accounts;
using PayLedger.Application.Common;
using PayLedger.Application.Payments;
using PayLedger.Domain.Transactions;

namespace PayLedger.IntegrationTests;

public class PaymentTests(ApiFactory factory)
{
    [Fact]
    public async Task Deposit_and_transfer_update_balances_and_the_ledger()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var a = await client.CreateWalletAsync();
        var b = await client.CreateWalletAsync();

        await client.DepositAsync(a.Id, 10_000);
        var response = await client.PostAsync("/v1/transfers", new { sourceAccountId = a.Id, destinationAccountId = b.Id, amount = 2_500, reference = "invoice-7" });
        var transfer = await MerchantClient.ReadAsync<TransactionDto>(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/v1/transactions/{transfer.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(TransactionType.Transfer, transfer.Type);
        Assert.Equal(7_500, (await client.GetAccountAsync(a.Id)).Balance);
        Assert.Equal(2_500, (await client.GetAccountAsync(b.Id)).Balance);

        var entries = await client.Http.GetFromJsonAsync<Page<LedgerEntryDto>>($"/v1/accounts/{a.Id}/entries", MerchantClient.Json);
        Assert.Equal([-2_500, 10_000], entries!.Data.Select(e => e.Amount));
        Assert.Equal([7_500, 10_000], entries.Data.Select(e => e.BalanceAfter));

        var fetched = await client.Http.GetFromJsonAsync<TransactionDto>($"/v1/transactions/{transfer.Id}", MerchantClient.Json);
        Assert.Equal("invoice-7", fetched!.Reference);
        Assert.Equal(0, fetched.Entries.Sum(e => e.Amount));
    }

    [Fact]
    public async Task Withdrawing_more_than_the_balance_fails_and_changes_nothing()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();
        await client.DepositAsync(wallet.Id, 1_000);

        var response = await client.PostAsync("/v1/withdrawals", new { accountId = wallet.Id, amount = 1_001 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("insufficient_funds", await MerchantClient.ErrorCodeAsync(response));
        Assert.Equal(1_000, (await client.GetAccountAsync(wallet.Id)).Balance);
    }

    [Fact]
    public async Task Cross_currency_transfer_converts_at_the_live_rate()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var usd = await client.CreateWalletAsync("USD");
        var pkr = await client.CreateWalletAsync("PKR");
        await client.DepositAsync(usd.Id, 10_000);

        var transfer = await MerchantClient.ReadAsync<TransactionDto>(
            await client.PostAsync("/v1/transfers", new { sourceAccountId = usd.Id, destinationAccountId = pkr.Id, amount = 10_000 }));

        Assert.Equal(278.45m, transfer.ExchangeRate);
        Assert.Equal(2_784_500, transfer.DestinationAmount);
        Assert.Equal(2_784_500, (await client.GetAccountAsync(pkr.Id)).Balance);
        Assert.All(transfer.Entries.GroupBy(e => e.Currency), g => Assert.Equal(0, g.Sum(e => e.Amount)));
    }

    [Fact]
    public async Task Rate_service_outage_returns_503_and_moves_nothing()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var jpy = await client.CreateWalletAsync("JPY");
        var usd = await client.CreateWalletAsync("USD");
        await client.DepositAsync(jpy.Id, 5_000);

        var response = await client.PostAsync("/v1/transfers", new { sourceAccountId = jpy.Id, destinationAccountId = usd.Id, amount = 100 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("exchange_rate_unavailable", await MerchantClient.ErrorCodeAsync(response));
        Assert.Equal(5_000, (await client.GetAccountAsync(jpy.Id)).Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_000_000_001)]
    public async Task Invalid_amounts_are_rejected_before_touching_the_ledger(long amount)
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();

        var response = await client.PostAsync("/v1/deposits", new { accountId = wallet.Id, amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unsupported_currency_is_a_clear_error()
    {
        var client = await MerchantClient.SignUpAsync(factory);

        var response = await client.Http.PostAsJsonAsync("/v1/accounts", new { name = "Bitcoin", currency = "BTC" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("unsupported_currency", await MerchantClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Deposits_into_settlement_accounts_are_refused()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var accounts = await client.Http.GetFromJsonAsync<Page<AccountDto>>("/v1/accounts?limit=100", MerchantClient.Json);
        var settlement = accounts!.Data[0];

        var response = await client.PostAsync("/v1/deposits", new { accountId = settlement.Id, amount = 100 });

        Assert.Equal("invalid_account", await MerchantClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Lists_page_newest_first_with_starting_after()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var wallet = await client.CreateWalletAsync();
        for (var i = 1; i <= 3; i++)
        {
            await client.DepositAsync(wallet.Id, i * 100);
        }

        var first = await client.Http.GetFromJsonAsync<Page<TransactionDto>>($"/v1/transactions?accountId={wallet.Id}&limit=2", MerchantClient.Json);
        var second = await client.Http.GetFromJsonAsync<Page<TransactionDto>>(
            $"/v1/transactions?accountId={wallet.Id}&limit=2&startingAfter={first!.Data[^1].Id}", MerchantClient.Json);

        Assert.True(first.HasMore);
        Assert.Equal([300, 200], first.Data.Select(t => t.Amount));
        Assert.False(second!.HasMore);
        Assert.Equal([100], second.Data.Select(t => t.Amount));
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PayLedger.Application.Accounts;
using PayLedger.Application.Common;
using PayLedger.Application.Merchants;
using PayLedger.Domain.Accounts;
using PayLedger.Domain.Common;

namespace PayLedger.IntegrationTests;

public class MerchantTests(ApiFactory factory)
{
    [Fact]
    public async Task Signup_returns_a_working_key_and_one_settlement_account_per_currency()
    {
        var client = await MerchantClient.SignUpAsync(factory);

        Assert.StartsWith(MerchantService.ApiKeyPrefix, client.Merchant.ApiKey, StringComparison.Ordinal);
        var me = await client.Http.GetFromJsonAsync<MerchantDto>("/v1/merchants/me", MerchantClient.Json);
        Assert.Equal(client.Merchant.Merchant.Id, me!.Id);

        var accounts = await client.Http.GetFromJsonAsync<Page<AccountDto>>("/v1/accounts?limit=100", MerchantClient.Json);
        Assert.Equal(Currencies.All.Order(), accounts!.Data.Where(a => a.Kind == AccountKind.Settlement).Select(a => a.Currency).Order());
    }

    [Fact]
    public async Task Email_is_unique_regardless_of_case()
    {
        var http = factory.CreateClient();
        var email = $"{Guid.NewGuid():N}@Example.com";
        (await http.PostAsJsonAsync("/v1/merchants", new { name = "One", email })).EnsureSuccessStatusCode();

        var again = await http.PostAsJsonAsync("/v1/merchants", new { name = "Two", email = email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", await MerchantClient.ErrorCodeAsync(again));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("pl_test_not-a-real-key")]
    [InlineData("sk_live_wrong-prefix")]
    public async Task Requests_without_a_valid_key_get_401(string? key)
    {
        var http = factory.CreateClient();
        if (key is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        var response = await http.GetAsync("/v1/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await MerchantClient.ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Rotating_the_key_disables_the_old_one()
    {
        var client = await MerchantClient.SignUpAsync(factory);

        var rotated = await MerchantClient.ReadAsync<MerchantWithKeyDto>(await client.Http.PostAsync("/v1/merchants/me/api-key", null));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Http.GetAsync("/v1/merchants/me")).StatusCode);
        client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.ApiKey);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/v1/merchants/me")).StatusCode);
    }

    [Fact]
    public async Task Merchants_cannot_see_each_others_accounts()
    {
        var alice = await MerchantClient.SignUpAsync(factory);
        var bob = await MerchantClient.SignUpAsync(factory);
        var aliceWallet = await alice.CreateWalletAsync();
        var bobWallet = await bob.CreateWalletAsync();
        await alice.DepositAsync(aliceWallet.Id, 1_000);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/v1/accounts/{aliceWallet.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/v1/accounts/{aliceWallet.Id}/entries")).StatusCode);
        var steal = await bob.PostAsync("/v1/transfers", new { sourceAccountId = aliceWallet.Id, destinationAccountId = bobWallet.Id, amount = 500 });
        Assert.Equal(HttpStatusCode.NotFound, steal.StatusCode);
        Assert.Equal(1_000, (await alice.GetAccountAsync(aliceWallet.Id)).Balance);
    }

    [Fact]
    public async Task Health_and_docs_are_public()
    {
        var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/openapi/v1.json")).StatusCode);
    }
}

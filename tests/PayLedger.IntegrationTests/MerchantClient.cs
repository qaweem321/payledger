using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PayLedger.Application.Accounts;
using PayLedger.Application.Common;
using PayLedger.Application.Merchants;
using PayLedger.Application.Payments;

namespace PayLedger.IntegrationTests;

/// <summary>An API client signed in as a brand-new merchant, so every test has its own data.</summary>
public sealed class MerchantClient(HttpClient http, MerchantWithKeyDto merchant)
{
    public static readonly JsonSerializerOptions Json = JsonDefaults.Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public HttpClient Http { get; } = http;
    public MerchantWithKeyDto Merchant { get; } = merchant;

    public static async Task<MerchantClient> SignUpAsync(ApiFactory factory)
    {
        var http = factory.CreateClient();
        var response = await http.PostAsJsonAsync("/v1/merchants", new { name = "Test Shop", email = $"{Guid.NewGuid():N}@example.com" });
        response.EnsureSuccessStatusCode();
        var merchant = (await response.Content.ReadFromJsonAsync<MerchantWithKeyDto>(Json))!;
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", merchant.ApiKey);
        return new MerchantClient(http, merchant);
    }

    public async Task<AccountDto> CreateWalletAsync(string currency = "USD", string name = "Wallet") =>
        await ReadAsync<AccountDto>(await Http.PostAsJsonAsync("/v1/accounts", new { name, currency }));

    public async Task<AccountDto> GetAccountAsync(long id) =>
        (await Http.GetFromJsonAsync<AccountDto>($"/v1/accounts/{id}", Json))!;

    public Task<HttpResponseMessage> PostAsync(string path, object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return Http.SendAsync(request);
    }

    public async Task<TransactionDto> DepositAsync(long accountId, long amount) =>
        await ReadAsync<TransactionDto>(await PostAsync("/v1/deposits", new { accountId, amount }));

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("code", out var code) ? code.GetString() : null;
}

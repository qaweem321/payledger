using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PayLedger.Application.Merchants;
using PayLedger.Infrastructure.Webhooks;

namespace PayLedger.IntegrationTests;

public class WebhookTests(ApiFactory factory)
{
    [Fact]
    public async Task Completed_transactions_are_sent_signed_to_the_merchants_webhook()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var url = $"http://receiver.test/hooks/{Guid.NewGuid():N}";
        var webhook = await MerchantClient.ReadAsync<WebhookSecretDto>(await client.Http.PutAsJsonAsync("/v1/merchants/me/webhook", new { url }));
        var wallet = await client.CreateWalletAsync();

        var deposit = await client.DepositAsync(wallet.Id, 4_200);

        var (request, body) = await WaitForWebhookAsync(url);
        var signature = request.Headers.GetValues(WebhookSigner.HeaderName).Single();
        Assert.True(WebhookSigner.Verify(webhook.Secret, signature, body, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
        Assert.Equal("transaction.completed", request.Headers.GetValues("PayLedger-Event").Single());

        using var payload = JsonDocument.Parse(body);
        Assert.Equal(deposit.Id, payload.RootElement.GetProperty("data").GetProperty("id").GetInt64());
        Assert.Equal("deposit", payload.RootElement.GetProperty("data").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Failed_deliveries_are_retried_later_not_dropped()
    {
        var client = await MerchantClient.SignUpAsync(factory);
        var url = $"http://receiver.test/fail/{Guid.NewGuid():N}";
        (await client.Http.PutAsJsonAsync("/v1/merchants/me/webhook", new { url })).EnsureSuccessStatusCode();
        var wallet = await client.CreateWalletAsync();

        await client.DepositAsync(wallet.Id, 100);

        await WaitForWebhookAsync(url);
        await Task.Delay(500);
        Assert.Single(factory.Webhooks.Received, r => r.Request.RequestUri!.ToString() == url); // next try is 30 s away
    }

    [Theory]
    [InlineData("ftp://example.com/hook")]
    [InlineData("not-a-url")]
    public async Task Bad_webhook_urls_are_refused(string url)
    {
        var client = await MerchantClient.SignUpAsync(factory);

        var response = await client.Http.PutAsJsonAsync("/v1/merchants/me/webhook", new { url });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    private async Task<(HttpRequestMessage Request, string Body)> WaitForWebhookAsync(string url)
    {
        for (var i = 0; i < 100; i++)
        {
            var match = factory.Webhooks.Received.FirstOrDefault(r => r.Request.RequestUri!.ToString() == url);
            if (match.Request is not null)
            {
                return match;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No webhook was sent to {url}.");
    }
}

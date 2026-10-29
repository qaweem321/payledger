using PayLedger.Domain.Webhooks;
using PayLedger.Infrastructure.Webhooks;

namespace PayLedger.UnitTests.Webhooks;

public class WebhookSignerTests
{
    private const string Secret = "whsec_test";
    private const string Body = """{"type":"transaction.completed"}""";
    private static readonly DateTimeOffset Now = TestAccounts.Now;

    [Fact]
    public void Signature_verifies_with_the_same_secret_and_body()
    {
        var header = WebhookSigner.Sign(Secret, Now.ToUnixTimeSeconds(), Body);

        Assert.StartsWith($"t={Now.ToUnixTimeSeconds()},v1=", header, StringComparison.Ordinal);
        Assert.True(WebhookSigner.Verify(Secret, header, Body, Now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Signature_fails_for_a_changed_body_wrong_secret_or_old_timestamp()
    {
        var header = WebhookSigner.Sign(Secret, Now.ToUnixTimeSeconds(), Body);

        Assert.False(WebhookSigner.Verify(Secret, header, Body + " ", Now, TimeSpan.FromMinutes(5)));
        Assert.False(WebhookSigner.Verify("whsec_other", header, Body, Now, TimeSpan.FromMinutes(5)));
        Assert.False(WebhookSigner.Verify(Secret, header, Body, Now.AddMinutes(6), TimeSpan.FromMinutes(5)));
        Assert.False(WebhookSigner.Verify(Secret, "garbage", Body, Now, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(5, 480)]
    [InlineData(20, 21_600)] // capped at 6 hours
    public void Retries_back_off_exponentially(int attempts, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), WebhookDelivery.Backoff(attempts));
}

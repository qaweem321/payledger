using Microsoft.Extensions.Options;
using PayLedger.Infrastructure.Webhooks;

namespace PayLedger.UnitTests.Webhooks;

public class WebhookUrlValidatorTests
{
    private static WebhookUrlValidator Validator(bool allowHttp = false, bool allowPrivate = false) =>
        new(Options.Create(new WebhookOptions { AllowHttp = allowHttp, AllowPrivateNetworks = allowPrivate }));

    [Theory]
    [InlineData("https://shop.example.com/hooks/payledger")]
    [InlineData("https://8.8.8.8/hook")]
    public void Public_https_urls_are_accepted(string url) => Assert.Null(Validator().Validate(url));

    [Theory]
    [InlineData("http://shop.example.com/hook")]
    [InlineData("ftp://shop.example.com/hook")]
    [InlineData("not a url")]
    [InlineData("https://user:pass@shop.example.com/hook")]
    [InlineData("https://localhost/hook")]
    [InlineData("https://api.localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/hook")]
    public void Unsafe_urls_are_rejected(string url) => Assert.NotNull(Validator().Validate(url));

    [Fact]
    public void Local_development_settings_allow_http_to_private_hosts() =>
        Assert.Null(Validator(allowHttp: true, allowPrivate: true).Validate("http://localhost:9000/hook"));
}

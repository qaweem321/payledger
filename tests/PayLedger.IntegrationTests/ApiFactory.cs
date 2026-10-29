using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;
using PayLedger.Infrastructure.Webhooks;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(PayLedger.IntegrationTests.ApiFactory))]

namespace PayLedger.IntegrationTests;

/// <summary>One real SQL Server container and one in-memory API for the whole test run.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2025-CU9-GDR1-ubuntu-24.04";

    private readonly MsSqlContainer _sql = new MsSqlBuilder(SqlServerImage).Build();

    public CapturingHandler Webhooks { get; } = new();

    public async ValueTask InitializeAsync()
    {
        await _sql.StartAsync();
        _ = Services; // Starts the host now, so migrations run once before any test.
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _sql.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("RateLimits:SignupsPerHour", "10000");
        builder.UseSetting("RateLimits:RequestsPerMinute", "10000");
        builder.UseSetting("Webhooks:AllowHttp", "true");
        builder.UseSetting("Webhooks:AllowPrivateNetworks", "true");
        builder.UseSetting("Webhooks:PollInterval", "00:00:00.100");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IExchangeRateProvider, FakeExchangeRates>();
            services.AddHttpClient(WebhookDispatcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => Webhooks);
        });
    }
}

public sealed class FakeExchangeRates : IExchangeRateProvider
{
    private static readonly Dictionary<(string, string), decimal> Rates = new()
    {
        [("USD", "PKR")] = 278.45m,
        [("USD", "EUR")] = 0.92m,
        [("EUR", "USD")] = 1.087m,
    };

    public Task<decimal> GetRateAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken) =>
        Rates.TryGetValue((baseCurrency, quoteCurrency), out var rate)
            ? Task.FromResult(rate)
            : throw new ExchangeRateUnavailableException($"No test rate for {baseCurrency}->{quoteCurrency}.");
}

/// <summary>Stands in for merchants' webhook receivers and records what PayLedger sent.</summary>
public sealed class CapturingHandler : HttpMessageHandler
{
    public ConcurrentQueue<(HttpRequestMessage Request, string Body)> Received { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Received.Enqueue((request, body));
        return new HttpResponseMessage(request.RequestUri!.AbsolutePath.Contains("fail", StringComparison.Ordinal)
            ? HttpStatusCode.InternalServerError
            : HttpStatusCode.OK);
    }
}

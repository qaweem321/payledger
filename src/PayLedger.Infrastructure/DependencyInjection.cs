using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Webhooks;
using PayLedger.Infrastructure.ExchangeRates;
using PayLedger.Infrastructure.Maintenance;
using PayLedger.Infrastructure.Persistence;
using PayLedger.Infrastructure.Webhooks;

namespace PayLedger.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Default is not set. See .env.example.");
        }

        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

        services.AddOptions<ExchangeRateOptions>().BindConfiguration(ExchangeRateOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddMemoryCache();
        services.AddHttpClient<IExchangeRateProvider, OpenExchangeRateProvider>((sp, http) =>
                http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ExchangeRateOptions>>().Value.BaseUrl))
            .AddStandardResilienceHandler();

        services.AddOptions<WebhookOptions>().BindConfiguration(WebhookOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IWebhookUrlValidator, WebhookUrlValidator>();
        services.AddHttpClient(WebhookDispatcher.HttpClientName, (sp, http) =>
            {
                http.Timeout = sp.GetRequiredService<IOptions<WebhookOptions>>().Value.RequestTimeout;
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PayLedger-Webhooks/1");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
                WebhookHttpHandler.Create(sp.GetRequiredService<IOptions<WebhookOptions>>().Value.AllowPrivateNetworks));
        services.AddSingleton<WebhookDispatcher>();
        if (configuration.GetValue($"{WebhookOptions.Section}:{nameof(WebhookOptions.DispatcherEnabled)}", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<WebhookDispatcher>());
        }

        services.AddHostedService<IdempotencyCleanupService>();
        return services;
    }

    /// <summary>Applies pending migrations. Used by Docker and local runs; production pipelines can run a migration bundle instead.</summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }
}

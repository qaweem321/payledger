using Microsoft.Extensions.DependencyInjection;
using PayLedger.Application.Accounts;
using PayLedger.Application.Fx;
using PayLedger.Application.Merchants;
using PayLedger.Application.Payments;
using PayLedger.Application.Transactions;
using PayLedger.Application.Webhooks;

namespace PayLedger.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MerchantService>();
        services.AddScoped<AccountService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<FxService>();
        services.AddScoped<IdempotencyExecutor>();
        services.AddScoped<WebhookOutbox>();
        return services;
    }
}

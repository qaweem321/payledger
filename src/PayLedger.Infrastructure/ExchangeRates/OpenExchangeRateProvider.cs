using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayLedger.Application.Abstractions;
using PayLedger.Application.Common;

namespace PayLedger.Infrastructure.ExchangeRates;

/// <summary>
/// Reads rates from ExchangeRate-API's open endpoint. One call returns every rate for a base
/// currency, so the whole table is cached and later conversions from that currency are free.
/// Retries, timeouts and the circuit breaker come from the standard resilience handler.
/// </summary>
public sealed partial class OpenExchangeRateProvider(
    HttpClient http,
    IMemoryCache cache,
    IOptions<ExchangeRateOptions> options,
    ILogger<OpenExchangeRateProvider> logger) : IExchangeRateProvider
{
    public async Task<decimal> GetRateAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken)
    {
        var table = await cache.GetOrCreateAsync($"fx:{baseCurrency}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.Value.CacheMinutes);
            return await FetchAsync(baseCurrency, cancellationToken);
        });

        return table!.TryGetValue(quoteCurrency, out var rate) && rate > 0
            ? rate
            : throw new ExchangeRateUnavailableException($"No exchange rate from {baseCurrency} to {quoteCurrency}.");
    }

    private async Task<Dictionary<string, decimal>> FetchAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.GetFromJsonAsync<RatesResponse>($"latest/{Uri.EscapeDataString(baseCurrency)}", cancellationToken);
            if (response is not { Result: "success", Rates: { } rates })
            {
                throw new ExchangeRateUnavailableException($"Exchange rate service returned no rates for {baseCurrency}.");
            }

            return rates;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
            or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            LogFetchFailed(logger, baseCurrency, ex);
            throw new ExchangeRateUnavailableException("The exchange rate service is unavailable. Try again shortly.", ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fetching exchange rates for {BaseCurrency} failed")]
    private static partial void LogFetchFailed(ILogger logger, string baseCurrency, Exception exception);

    private sealed record RatesResponse(
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("rates")] Dictionary<string, decimal>? Rates);
}

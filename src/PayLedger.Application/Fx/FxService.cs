using PayLedger.Application.Abstractions;
using PayLedger.Domain.Common;

namespace PayLedger.Application.Fx;

public sealed record FxQuoteDto(string From, string To, decimal Rate, long Amount, long ConvertedAmount);

public sealed class FxService(IExchangeRateProvider rates)
{
    public async Task<FxQuoteDto> QuoteAsync(string from, string to, long amount, CancellationToken ct)
    {
        from = from.ToUpperInvariant();
        to = to.ToUpperInvariant();
        foreach (var code in new[] { from, to })
        {
            if (!Currencies.IsSupported(code))
            {
                throw new DomainException("unsupported_currency", $"Currency '{code}' is not supported.");
            }
        }

        var rate = from == to ? 1m : await rates.GetRateAsync(from, to, ct);
        return new FxQuoteDto(from, to, rate, amount, Currencies.Convert(amount, from, to, rate));
    }
}

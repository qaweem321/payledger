namespace PayLedger.Application.Abstractions;

public interface IExchangeRateProvider
{
    /// <summary>How many units of <paramref name="quoteCurrency"/> one unit of <paramref name="baseCurrency"/> buys.</summary>
    /// <exception cref="Common.ExchangeRateUnavailableException">The rate service cannot answer.</exception>
    Task<decimal> GetRateAsync(string baseCurrency, string quoteCurrency, CancellationToken cancellationToken);
}

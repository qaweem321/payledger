using System.ComponentModel.DataAnnotations;

namespace PayLedger.Infrastructure.ExchangeRates;

public sealed class ExchangeRateOptions
{
    public const string Section = "ExchangeRates";

    /// <summary>Base address of an ExchangeRate-API compatible service (no key needed for the open endpoint).</summary>
    [Required, Url]
    public string BaseUrl { get; set; } = "https://open.er-api.com/v6/";

    /// <summary>The open endpoint updates once a day, so an hour of caching costs nothing in accuracy.</summary>
    [Range(1, 1440)]
    public int CacheMinutes { get; set; } = 60;
}

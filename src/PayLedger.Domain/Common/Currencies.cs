namespace PayLedger.Domain.Common;

/// <summary>Supported ISO 4217 currencies and how many minor units (decimal places) each one has.</summary>
public static class Currencies
{
    private static readonly Dictionary<string, int> MinorUnitDigits = new(StringComparer.Ordinal)
    {
        ["USD"] = 2,
        ["EUR"] = 2,
        ["GBP"] = 2,
        ["PKR"] = 2,
        ["AED"] = 2,
        ["SAR"] = 2,
        ["JPY"] = 0,
        ["KWD"] = 3,
    };

    public static IReadOnlyCollection<string> All => MinorUnitDigits.Keys;

    public static bool IsSupported(string? code) => code is not null && MinorUnitDigits.ContainsKey(code);

    public static int Digits(string code) =>
        MinorUnitDigits.TryGetValue(code, out var digits)
            ? digits
            : throw new DomainException("unsupported_currency", $"Currency '{code}' is not supported.");

    /// <summary>
    /// Converts an amount in minor units between currencies. The result is rounded down, so a
    /// conversion never creates money out of rounding.
    /// </summary>
    public static long Convert(long amount, string from, string to, decimal rate)
    {
        if (rate <= 0)
        {
            throw new DomainException("invalid_rate", "Exchange rate must be positive.");
        }

        var scale = Pow10(Digits(to) - Digits(from));
        var converted = decimal.Floor(amount * rate * scale);
        return converted > long.MaxValue
            ? throw new DomainException("amount_too_large", "Converted amount is too large.")
            : (long)converted;
    }

    private static decimal Pow10(int exponent)
    {
        var result = 1m;
        for (var i = 0; i < Math.Abs(exponent); i++)
        {
            result *= 10;
        }

        return exponent >= 0 ? result : 1 / result;
    }
}

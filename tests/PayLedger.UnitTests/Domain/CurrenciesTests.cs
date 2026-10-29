using PayLedger.Domain.Common;

namespace PayLedger.UnitTests.Domain;

public class CurrenciesTests
{
    [Theory]
    [InlineData(10_000, "USD", "EUR", 0.92, 9_200)]      // $100.00 -> 92.00 EUR
    [InlineData(10_000, "USD", "JPY", 151.237, 15_123)]  // $100.00 -> 15123 JPY (no minor units)
    [InlineData(15_123, "JPY", "USD", 0.006612, 9_999)]  // 15123 JPY -> $99.99, rounded down
    [InlineData(10_000, "USD", "KWD", 0.30712, 30_712)]  // $100.00 -> 30.712 KWD (3 decimals)
    [InlineData(1, "USD", "PKR", 278.45, 278)]           // 1 cent -> 2.78 PKR
    public void Convert_uses_minor_units_and_rounds_down(long amount, string from, string to, decimal rate, long expected) =>
        Assert.Equal(expected, Currencies.Convert(amount, from, to, rate));

    [Fact]
    public void Convert_rejects_non_positive_rates() =>
        Assert.Equal("invalid_rate", Assert.Throws<DomainException>(() => Currencies.Convert(100, "USD", "EUR", 0)).Code);

    [Fact]
    public void Unknown_currency_is_rejected()
    {
        Assert.False(Currencies.IsSupported("XYZ"));
        Assert.False(Currencies.IsSupported("usd"));
        Assert.Equal("unsupported_currency", Assert.Throws<DomainException>(() => Currencies.Digits("XYZ")).Code);
    }
}

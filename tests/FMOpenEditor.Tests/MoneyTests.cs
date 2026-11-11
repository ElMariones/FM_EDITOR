using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Tests;

/// <summary>Amounts are stored in pounds and shown in the currency chosen in Settings.</summary>
[Collection("Money")]
public class MoneyTests : IDisposable
{
    public void Dispose() => Money.Current = Money.Pound;

    [Fact]
    public void Pounds_are_shown_as_stored()
    {
        Money.Current = Money.Pound;
        Assert.Equal("£1.5M", Money.Format(1_500_000));
        Assert.Equal("-£250K", Money.Format(-250_000));
        Assert.Equal("£9,500", Money.Format(9_500));
        Assert.Equal(1_500_000, Money.Parse("£1.5m"));
    }

    [Fact]
    public void Another_currency_converts_both_ways_at_its_rate()
    {
        Money.Current = new CurrencyDisplay(28, "European Euro", "€", "Euro", 1.2);
        Assert.Equal("€1.2M", Money.Format(1_000_000));
        Assert.Equal(1_000_000, Money.Parse("€1.2M"));
        Assert.Equal(1_000_000, Money.Parse("1,200,000"));
        Assert.Equal(1_000_000, Money.ToBase(Money.ToDisplay(1_000_000)));
    }

    [Fact]
    public void A_currency_without_a_symbol_is_named_after_the_amount()
    {
        Money.Current = new CurrencyDisplay(78, "Swiss Franc", "", "Franc", 1.1);
        Assert.Equal("1.1M Franc", Money.Format(1_000_000));
        Assert.Equal(1_000_000, Money.Parse("1.1M Franc"));
    }

    [Fact]
    public void A_broken_rate_falls_back_to_pounds()
    {
        Money.Current = new CurrencyDisplay(1, "Broken", "?", "?", 0);
        Assert.Same(Money.Pound, Money.Current);
    }
}

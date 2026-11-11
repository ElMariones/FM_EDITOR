using System.Globalization;

namespace FMOpenEditor.Core.Model;

/// <summary>
/// A currency money is shown in. <paramref name="Rate"/> is how many units one pound buys, which is
/// how the game stores its own exchange rates.
/// </summary>
/// <param name="Id">The game's id for the currency record.</param>
/// <param name="Symbol">Written before the amount ("€"); empty when the currency has none.</param>
/// <param name="ShortName">Written after the amount when there is no symbol ("Franc").</param>
public sealed record CurrencyDisplay(int Id, string Name, string Symbol, string ShortName, double Rate)
{
    public override string ToString() => Name;
}

/// <summary>
/// The game keeps every amount in pounds. This converts to and from the currency the user chose to
/// read them in; nothing else in the editor needs to know which one that is.
/// </summary>
public static class Money
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Ids are the game's own. The rates are only used until a save is open (and for the demo save);
    // a live save supplies its own.
    public static readonly CurrencyDisplay Pound = new(86, "United Kingdom Pound", "£", "Pound", 1);
    public static readonly CurrencyDisplay Euro = new(28, "European Euro", "€", "Euro", 1.16);
    public static readonly CurrencyDisplay Dollar = new(87, "United States Dollar", "$", "Dollar", 1.35);

    /// <summary>The three offered up front; the rest are behind "More".</summary>
    public static readonly IReadOnlyList<CurrencyDisplay> Common = [Euro, Pound, Dollar];

    private static CurrencyDisplay _current = Pound;

    /// <summary>The currency amounts are shown and typed in.</summary>
    public static CurrencyDisplay Current
    {
        get => _current;
        set => _current = value.Rate > 0 && double.IsFinite(value.Rate) ? value : Pound;
    }

    /// <summary>Pounds to the chosen currency.</summary>
    public static double ToDisplay(double pounds) => pounds * Current.Rate;

    /// <summary>The chosen currency back to pounds, as the game stores it.</summary>
    public static long ToBase(double shown) => (long)Math.Round(shown / Current.Rate);

    /// <summary>A short amount such as "€1.5M" or "250K Franc".</summary>
    public static string Format(long pounds)
    {
        var shown = ToDisplay(pounds);
        var abs = Math.Abs(shown);
        var number = abs switch
        {
            >= 1_000_000_000 => (abs / 1_000_000_000d).ToString("0.##", Inv) + "B",
            >= 1_000_000 => (abs / 1_000_000d).ToString("0.##", Inv) + "M",
            >= 10_000 => (abs / 1_000d).ToString("0.#", Inv) + "K",
            _ => abs.ToString("N0", Inv),
        };
        var sign = shown < 0 && number != "0" ? "-" : "";
        var c = Current;
        return c.Symbol.Length > 0 ? $"{sign}{c.Symbol}{number}"
            : c.ShortName.Length > 0 ? $"{sign}{number} {c.ShortName}"
            : sign + number;
    }

    /// <summary>Reads what a user typed (1500000, 1.5M, 750k, €2,000,000) as pounds.</summary>
    public static long Parse(string text)
    {
        var c = Current;
        var t = text.Trim();
        // Longest first, so "R$" goes before "$".
        foreach (var mark in new[] { c.Symbol, c.ShortName, "€", "£", "$" }.Where(m => m.Length > 0).OrderByDescending(m => m.Length))
            t = t.Replace(mark, "", StringComparison.OrdinalIgnoreCase);
        t = t.Replace(",", "").Replace(" ", "").ToLowerInvariant();
        double mult = 1;
        if (t.EndsWith('k')) { mult = 1e3; t = t[..^1]; }
        else if (t.EndsWith('m')) { mult = 1e6; t = t[..^1]; }
        else if (t.EndsWith('b')) { mult = 1e9; t = t[..^1]; }
        return ToBase(double.Parse(t, Inv) * mult);
    }
}

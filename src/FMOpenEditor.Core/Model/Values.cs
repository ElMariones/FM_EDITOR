using System.Globalization;
using System.Text.Json;

namespace FMOpenEditor.Core.Model;

/// <summary>Converts, clamps, compares and formats property values by their <see cref="ValueKind"/>.</summary>
public static class Values
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Converts any reasonable input (boxed number, string, JSON element) to the canonical type for
    /// <paramref name="def"/>, clamped to its range. Throws <see cref="FormatException"/> if it can't.
    /// </summary>
    public static object? Coerce(PropertyDef def, object? raw)
    {
        if (raw is JsonElement json) raw = FromJson(json);
        if (raw is null || raw is string { Length: 0 } && def.Kind is not ValueKind.Text)
        {
            return def.Kind switch
            {
                ValueKind.Date or ValueKind.Reference => null,
                ValueKind.Text => "",
                _ => throw new FormatException($"{def.Label} needs a value."),
            };
        }

        return def.Kind switch
        {
            ValueKind.Integer or ValueKind.Choice => (int)Math.Clamp(Math.Round(ToDouble(raw)), def.Min, def.Max),
            ValueKind.Money => (long)Math.Clamp(Math.Round(ToDouble(raw)), def.Min, def.Max),
            ValueKind.Decimal => Math.Clamp(ToDouble(raw), def.Min, def.Max),
            ValueKind.Boolean => raw switch
            {
                bool b => b,
                string s => bool.Parse(s),
                _ => ToDouble(raw) != 0,
            },
            ValueKind.Text => Convert.ToString(raw, Inv) ?? "",
            ValueKind.Date => raw switch
            {
                DateOnly d => d,
                DateTime dt => DateOnly.FromDateTime(dt),
                DateTimeOffset dto => DateOnly.FromDateTime(dto.Date),
                string s => DateOnly.Parse(s, Inv),
                _ => throw new FormatException($"{def.Label} needs a date."),
            },
            ValueKind.Reference => raw is int i && i < 0 ? null : (int)ToDouble(raw),
            _ => raw,
        };
    }

    public static double ToDouble(object raw) => raw switch
    {
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        short s => s,
        byte b => b,
        bool b => b ? 1 : 0,
        decimal m => (double)m,
        string s => double.Parse(s.Replace(",", "").Replace("_", ""), NumberStyles.Float, Inv),
        _ => Convert.ToDouble(raw, Inv),
    };

    private static object? FromJson(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => e.GetRawText(),
    };

    /// <summary>Orders two values of the same property; nulls sort first.</summary>
    public static int Compare(object? a, object? b)
    {
        if (a is null || b is null) return (a is null ? 0 : 1) - (b is null ? 0 : 1);
        return (a, b) switch
        {
            (string x, string y) => string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase),
            (DateOnly x, DateOnly y) => x.CompareTo(y),
            (bool x, bool y) => x.CompareTo(y),
            _ => ToDouble(a).CompareTo(ToDouble(b)),
        };
    }

    public static bool AreEqual(object? a, object? b) => Compare(a, b) == 0;

    public static string Format(PropertyDef def, object? value, Func<EntityKind, int, string>? nameOf = null) =>
        value switch
        {
            null => def.Kind == ValueKind.Reference ? "—" : "",
            long money when def.Kind == ValueKind.Money => FormatMoney(money),
            int idx when def.Kind == ValueKind.Choice && def.Choices is { } c && idx >= 0 && idx < c.Count => c[idx],
            int id when def.Kind == ValueKind.Reference && nameOf is not null && def.RefKind is { } k => nameOf(k, id),
            bool b => b ? "Yes" : "No",
            DateOnly d => d.ToString("dd MMM yyyy", Inv),
            double d => d.ToString("0.##", Inv),
            _ => Convert.ToString(value, Inv) ?? "",
        };

    public static string FormatMoney(long amount)
    {
        var abs = Math.Abs(amount);
        var sign = amount < 0 ? "-" : "";
        return abs switch
        {
            >= 1_000_000_000 => $"{sign}€{abs / 1_000_000_000d:0.##}B",
            >= 1_000_000 => $"{sign}€{abs / 1_000_000d:0.##}M",
            >= 10_000 => $"{sign}€{abs / 1_000d:0.#}K",
            _ => $"{sign}€{abs:N0}",
        };
    }
}

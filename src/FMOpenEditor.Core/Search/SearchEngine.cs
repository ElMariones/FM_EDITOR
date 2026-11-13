using System.Text.Json.Serialization;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Search;

/// <summary><see cref="OneOf"/> takes a '|'-separated list, e.g. "England|Scotland".</summary>
public enum FilterOp { Equals, NotEquals, AtLeast, AtMost, Between, Contains, IsEmpty, IsNotEmpty, OneOf }

/// <summary>One condition on one property, e.g. Age AtMost 21 or Contract.Expiry AtMost 2027-06-30.</summary>
public sealed record Filter(string Property, FilterOp Op, object? Value = null, object? Value2 = null)
{
    [JsonIgnore] public string Description => Op switch
    {
        FilterOp.Between => $"{Property} between {Value} and {Value2}",
        FilterOp.IsEmpty or FilterOp.IsNotEmpty => $"{Property} {Op}",
        _ => $"{Property} {Op} {Value}",
    };
}

public sealed class SearchQuery
{
    public EntityKind Kind { get; init; }

    /// <summary>Free text matched against the display name (accent- and case-insensitive).</summary>
    public string? Text { get; init; }
    public List<Filter> Filters { get; init; } = [];
    public int Limit { get; init; } = 5000;

    /// <summary>
    /// Orders every match before <see cref="Limit"/> is applied, so "top 5,000 by CA" really is the
    /// top of the whole database rather than the first 5,000 read.
    /// </summary>
    public string? SortKey { get; init; }
    public bool SortDescending { get; init; }
}

public static class SearchEngine
{
    public static List<Entity> Run(EditorSession session, SearchQuery query)
    {
        var text = Normalize(query.Text);
        var filters = query.Filters
            .Select(f => (Filter: f, Def: PropertyCatalog.Get(query.Kind, f.Property)))
            .Where(t => session.IsSupported(query.Kind, t.Filter.Property))
            .Select(t => (t.Filter, t.Def, A: CoerceOrNull(t.Def, t.Filter.Value), B: CoerceOrNull(t.Def, t.Filter.Value2)))
            .ToList();

        var sortKey = query.SortKey is { } k && session.IsSupported(query.Kind, k) ? k : null;
        var results = new List<Entity>();
        foreach (var e in session.GetAll(query.Kind))
        {
            if (text.Length > 0 && !session.SearchNameOf(e).Contains(text, StringComparison.Ordinal)) continue;
            if (!filters.All(f => Matches(session.TryRead(e, f.Filter.Property), f.Filter.Op, f.A, f.B))) continue;
            results.Add(e);
            if (sortKey is null && results.Count >= query.Limit) break;
        }
        if (sortKey is null) return results;

        var keyed = results.Select(e => (Entity: e, Key: SortValue(session, e, sortKey))).ToList();
        var sign = query.SortDescending ? -1 : 1;
        keyed.Sort((a, b) => Values.Compare(a.Key, b.Key) * sign);
        return keyed.Take(query.Limit).Select(t => t.Entity).ToList();
    }

    /// <summary>The value a column sorts by: references sort by the name they show, not their id.</summary>
    public static object? SortValue(EditorSession session, Entity e, string key)
    {
        var def = PropertyCatalog.Get(e.Kind, key);
        var v = session.TryRead(e, key);
        return def.Kind == ValueKind.Reference && v is int id && def.RefKind is { } k ? session.NameOf(k, id) : v;
    }

    public static bool Matches(PropertyDef def, object? value, Filter filter) =>
        Matches(value, filter.Op, CoerceOrNull(def, filter.Value), CoerceOrNull(def, filter.Value2));

    private static bool Matches(object? v, FilterOp op, object? a, object? b) => op switch
    {
        FilterOp.IsEmpty => v is null or "",
        FilterOp.IsNotEmpty => v is not (null or ""),
        FilterOp.Contains => v is string s && a is string t && Normalize(s).Contains(Normalize(t)),
        _ when v is null => false,
        FilterOp.OneOf => a is string list && list.Split('|').Any(x => Normalize(x) == Normalize(Convert.ToString(v))),
        FilterOp.Equals => Values.AreEqual(v, a),
        FilterOp.NotEquals => !Values.AreEqual(v, a),
        FilterOp.AtLeast => Values.Compare(v, a) >= 0,
        FilterOp.AtMost => Values.Compare(v, a) <= 0,
        FilterOp.Between => Values.Compare(v, a) >= 0 && Values.Compare(v, b) <= 0,
        _ => false,
    };

    private static object? CoerceOrNull(PropertyDef def, object? raw)
    {
        if (raw is null) return null;
        if (raw is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } js) raw = js.GetString() ?? "";
        // Text filters compare as entered.
        if (def.Kind == ValueKind.Text || raw is string s && s.Contains('|')) return raw!.ToString();
        try { return Values.Coerce(def with { Min = double.MinValue, Max = double.MaxValue }, raw); }
        catch (FormatException) { return null; }
    }

    /// <summary>Lower-case and strip accents so "fernandez" finds "Fernández".</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var decomposed = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(decomposed.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
    }
}

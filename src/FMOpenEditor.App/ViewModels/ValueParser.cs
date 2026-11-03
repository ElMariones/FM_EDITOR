using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.App.ViewModels;

/// <summary>Turns what the user typed into a value for a property: names for references, labels for choices.</summary>
public static class ValueParser
{
    public static object? Parse(EditorSession session, PropertyDef def, string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return def.Kind is ValueKind.Text ? "" : null;

        switch (def.Kind)
        {
            case ValueKind.Reference when def.RefKind is { } kind:
                if (int.TryParse(text, out var id)) return id;
                var wanted = SearchEngine.Normalize(text);
                var match = session.GetAll(kind)
                    .FirstOrDefault(e => SearchEngine.Normalize(session.NameOf(kind, e.Id)) == wanted)
                    ?? session.GetAll(kind).FirstOrDefault(e => SearchEngine.Normalize(session.NameOf(kind, e.Id)).Contains(wanted));
                return match?.Id ?? throw new FormatException($"No {kind.ToString().ToLowerInvariant()} called '{text}'.");

            case ValueKind.Choice when def.Choices is { } choices:
                var idx = choices.ToList().FindIndex(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) return idx;
                return int.TryParse(text, out var n) ? n : throw new FormatException($"'{text}' isn't a valid {def.Label}.");

            case ValueKind.Boolean:
                return text.ToLowerInvariant() switch
                {
                    "yes" or "y" or "true" or "1" or "on" => true,
                    "no" or "n" or "false" or "0" or "off" => false,
                    _ => throw new FormatException("Use yes or no."),
                };

            case ValueKind.Money:
                return ParseMoney(text);

            default:
                return text;
        }
    }

    /// <summary>Accepts 1500000, 1.5M, 750k, €2,000,000.</summary>
    public static long ParseMoney(string text)
    {
        var t = text.Replace("€", "").Replace("£", "").Replace("$", "").Replace(",", "").Replace(" ", "").ToLowerInvariant();
        double mult = 1;
        if (t.EndsWith('k')) { mult = 1e3; t = t[..^1]; }
        else if (t.EndsWith('m')) { mult = 1e6; t = t[..^1]; }
        else if (t.EndsWith('b')) { mult = 1e9; t = t[..^1]; }
        return (long)Math.Round(double.Parse(t, System.Globalization.CultureInfo.InvariantCulture) * mult);
    }
}

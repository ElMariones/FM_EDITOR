using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

public sealed record CompareRow(string Category, string Label, string A, string B, bool ABetter, bool BBetter);

/// <summary>Two players (or staff, clubs...) side by side; the better value of each attribute is highlighted.</summary>
public sealed class CompareViewModel
{
    public string NameA { get; }
    public string NameB { get; }
    public IReadOnlyList<CompareRow> Rows { get; }

    public CompareViewModel(EditorSession session, Entity a, Entity b)
    {
        NameA = session.TryRead(a, PropertyCatalog.DisplayName) as string ?? a.ToString();
        NameB = session.TryRead(b, PropertyCatalog.DisplayName) as string ?? b.ToString();

        Rows = PropertyCatalog.For(a.Kind)
            .Where(p => p.Key != PropertyCatalog.DisplayName && session.IsSupported(a.Kind, p.Key))
            .Select(p =>
            {
                var va = session.TryRead(a, p.Key);
                var vb = session.TryRead(b, p.Key);
                var cmp = p.IsNumeric ? Values.Compare(va, vb) : 0;
                // Lower is better for a few hidden traits.
                if (p.Key is "Attr.InjuryProneness" or "Attr.Dirtiness" or "Attr.Controversy" or "Jadedness") cmp = -cmp;
                return new CompareRow(p.Category, p.Label, Values.Format(p, va, session.NameOf),
                    Values.Format(p, vb, session.NameOf), cmp > 0, cmp < 0);
            })
            .ToList();
    }
}

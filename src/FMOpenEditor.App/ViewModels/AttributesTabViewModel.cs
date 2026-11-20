using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

/// <summary>One FM role and the attributes the game highlights for it (from roles.json).</summary>
public sealed record RoleDef(string Name, string Position, string[] Positions, bool InPossession,
    string[] Key, string[] Preferred, string[]? Unnecessary)
{
    public override string ToString() => Name;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Roles shipped next to the app; empty when the file is missing.</summary>
    public static readonly Lazy<IReadOnlyList<RoleDef>> All = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "roles.json");
        if (!File.Exists(path)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("roles").Deserialize<List<RoleDef>>(Json) ?? [];
    });
}

/// <summary>An attribute's editor with its role level: 2 key, 1 preferable, -1 unnecessary, 0 other.</summary>
public sealed record RoleAttributeRow(PropertyEditorViewModel Editor, int Level)
{
    public bool IsKey => Level == 2;
    public bool IsPreferred => Level == 1;
    public bool IsUnnecessary => Level == -1;
}

public sealed record RoleAttributeColumn(string Name, IReadOnlyList<RoleAttributeRow> Rows);

/// <summary>
/// The player's Attributes tab: Technical, Mental, Physical (and Goalkeeping for keepers) side by
/// side and editable, with a chosen role's key attributes green and preferable ones blue, like the game.
/// </summary>
public sealed partial class AttributesTabViewModel : EditorTabViewModel
{
    public static readonly string[] Categories = ["Goalkeeping", "Technical", "Mental", "Physical"];
    private readonly IReadOnlyList<PropertyGroupViewModel> _groups;

    public AttributesTabViewModel(EditorSession session, Entity player, IReadOnlyList<PropertyGroupViewModel> groups, IReadOnlyList<RoleDef> roles)
        : base("Attributes")
    {
        _groups = groups.Where(g => Categories.Contains(g.Name)).OrderBy(g => Array.IndexOf(Categories, g.Name)).ToList();
        Roles = roles;
        // Start on the first role for the player's best position.
        var best = PropertyCatalog.PlayerPositions.MaxBy(p => session.TryRead(player, $"Pos.{p}") is int v ? v : 0);
        _selectedRole = roles.FirstOrDefault(r => r.Positions.Contains(best)) ?? roles.FirstOrDefault();
        Positions = roles.Select(r => r.Position).Distinct().ToList();
        _selectedPosition = _selectedRole?.Position;
        Refresh();
    }

    public IReadOnlyList<RoleDef> Roles { get; }
    public bool HasRoles => Roles.Count > 0;

    /// <summary>The first box picks a position ("D (C)"), the second a role for it.</summary>
    public IReadOnlyList<string> Positions { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PositionRoles))] private string? _selectedPosition;
    public IReadOnlyList<RoleDef> PositionRoles => Roles.Where(r => r.Position == SelectedPosition).ToList();

    partial void OnSelectedPositionChanged(string? value)
    {
        OnPropertyChanged(nameof(PositionRoles));
        if (SelectedRole?.Position != value) SelectedRole = PositionRoles.FirstOrDefault();
    }

    [ObservableProperty] private RoleDef? _selectedRole;
    [ObservableProperty] private IReadOnlyList<RoleAttributeColumn> _columns = [];

    // The role box writes null while its list is swapped; keep the position then.
    partial void OnSelectedRoleChanged(RoleDef? value)
    {
        if (value is not null) SelectedPosition = value.Position;
        Refresh();
    }

    private void Refresh()
    {
        var r = SelectedRole;
        int Level(PropertyDef d)
        {
            if (r is null) return 0;
            var a = d.Key[5..]; // "Attr.Heading" -> "Heading"
            return r.Key.Contains(a) ? 2 : r.Preferred.Contains(a) ? 1 : r.Unnecessary?.Contains(a) == true ? -1 : 0;
        }
        var listed = r is null ? null : r.Key.Concat(r.Preferred).Select(a => "Attr." + a).ToHashSet();
        Columns = _groups
            // Goalkeeping only for keeper roles: shown when the role lists one of its attributes.
            .Where(g => g.Name != "Goalkeeping" || listed is null || g.Editors.Any(e => listed.Contains(e.Def.Key)))
            .Select(g => new RoleAttributeColumn(g.Name, g.Editors.Select(e => new RoleAttributeRow(e, Level(e.Def))).ToList()))
            .ToList();
    }
}

/// <summary>A tab made of several field groups, each under its own heading (Overview: Profile, Looks…).</summary>
public sealed class SectionsTabViewModel(string name, IReadOnlyList<PropertyGroupViewModel> sections) : EditorTabViewModel(name)
{
    public IReadOnlyList<PropertyGroupViewModel> Sections { get; } = sections;
    public bool ShowHeadings => Sections.Count > 1;
}

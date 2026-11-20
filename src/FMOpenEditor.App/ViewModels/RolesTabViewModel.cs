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

/// <summary>An attribute in the role view: 2 key, 1 preferable, -1 unnecessary, 0 other.</summary>
public sealed record RoleAttributeRow(string Label, string Value, int Level)
{
    public bool IsKey => Level == 2;
    public bool IsPreferred => Level == 1;
    public bool IsUnnecessary => Level == -1;
}

public sealed record RoleAttributeColumn(string Name, IReadOnlyList<RoleAttributeRow> Rows);

/// <summary>The Roles tab: pick a role, see the player's attributes with that role's key and preferable ones highlighted.</summary>
public sealed partial class RolesTabViewModel : EditorTabViewModel
{
    private static readonly string[] Order = ["Goalkeeping", "Technical", "Mental", "Physical"];
    private readonly EditorSession _session;
    private readonly Entity _player;

    public RolesTabViewModel(EditorSession session, Entity player, IReadOnlyList<RoleDef> roles) : base("Roles")
    {
        _session = session;
        _player = player;
        Roles = roles;
        // Start on the first role for the player's best position.
        var best = PropertyCatalog.PlayerPositions.MaxBy(p => session.TryRead(player, $"Pos.{p}") is int v ? v : 0);
        _selectedRole = roles.FirstOrDefault(r => r.Positions.Contains(best)) ?? roles.FirstOrDefault();
        Positions = roles.Select(r => r.Position).Distinct().ToList();
        _selectedPosition = _selectedRole?.Position;
        Refresh();
    }

    public IReadOnlyList<RoleDef> Roles { get; }

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

    public void Refresh()
    {
        if (SelectedRole is not { } r) { Columns = []; return; }
        int Level(string a) => r.Key.Contains(a) ? 2 : r.Preferred.Contains(a) ? 1 : r.Unnecessary?.Contains(a) == true ? -1 : 0;
        var listed = r.Key.Concat(r.Preferred).ToHashSet();
        Columns = PropertyCatalog.For(EntityKind.Player)
            .Where(p => p.IsAttribute && Order.Contains(p.Category) && _session.IsSupported(EntityKind.Player, p.Key))
            .GroupBy(p => p.Category)
            // Goalkeeping only for keeper roles: shown when the role lists one of its attributes.
            .Where(g => g.Key != "Goalkeeping" || g.Any(p => listed.Contains(p.Key[5..])))
            .OrderBy(g => Array.IndexOf(Order, g.Key))
            .Select(g => new RoleAttributeColumn(g.Key,
                g.Select(p => new RoleAttributeRow(p.Label, _session.Format(_player, p.Key), Level(p.Key[5..]))).ToList()))
            .ToList();
    }
}

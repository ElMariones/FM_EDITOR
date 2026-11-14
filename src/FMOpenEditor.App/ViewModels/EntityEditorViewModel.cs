using CommunityToolkit.Mvvm.ComponentModel;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

public sealed class PropertyGroupViewModel(string name, IReadOnlyList<PropertyEditorViewModel> editors) : EditorTabViewModel(name)
{
    public IReadOnlyList<PropertyEditorViewModel> Editors { get; } = editors;

    public bool IsAttributeGrid => Editors.Count > 0 && Editors.All(e => e.Def.IsAttribute);
}

/// <summary>The detail panel: every supported property of one entity, grouped into tabs by category.</summary>
public sealed partial class EntityEditorViewModel : ObservableObject
{
    private readonly EditorSession _session;

    public Entity Entity { get; }
    /// <summary>The field groups (Profile, Technical, …).</summary>
    public IReadOnlyList<PropertyGroupViewModel> Groups { get; }

    /// <summary>Every tab: the field groups, then who points here (a club's players, a nation's clubs).</summary>
    public IReadOnlyList<EditorTabViewModel> Tabs { get; }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string? _watchState;
    [ObservableProperty] private EditorTabViewModel? _selectedGroup;

    /// <summary>The face or badge shown in the header; hidden when the user has none.</summary>
    public EntityPicture Picture { get; }

    public bool IsFrozen => _session.Watcher.ModeOf(Entity) == WatchMode.Freeze;
    public bool IsOnlyUp => _session.Watcher.ModeOf(Entity) == WatchMode.OnlyUp;

    public int UnsupportedCount { get; }
    public string? UnsupportedText => UnsupportedCount > 0 ? $"{UnsupportedCount} fields not mapped for this game version yet" : null;

    /// <summary>Record base address, for contributors working out offsets in Research.</summary>
    public string? AddressText => Entity.Address != 0 ? $"Record at 0x{Entity.Address:X}" : null;

    public EntityEditorViewModel(EditorSession session, Entity entity) : this(session, entity, null, null) { }

    /// <param name="relations">Reverse lookups for the related-list tabs; none are shown without it.</param>
    /// <param name="open">Opens another entity (a reference, a related row) in its own page.</param>
    public EntityEditorViewModel(EditorSession session, Entity entity, RelationIndex? relations, Action<Entity>? open)
    {
        _session = session;
        Entity = entity;
        Picture = new EntityPicture(entity, FMOpenEditor.Core.Graphics.GraphicSize.Large);

        var all = PropertyCatalog.For(entity.Kind);
        var supported = all.Where(p => session.IsSupported(entity.Kind, p.Key)).ToList();
        UnsupportedCount = all.Count - supported.Count;

        Groups = supported
            .Where(p => p.Key != PropertyCatalog.DisplayName)
            // A bonus the contract lacks can't be added in a live game (no room in its list), so it is not shown.
            .Where(p => p.Category != "Bonuses" || session.TryRead(entity, p.Key) is not null)
            .GroupBy(p => p.Category)
            .Select(g => new PropertyGroupViewModel(g.Key, g.Select(p => PropertyEditorViewModel.Create(session, entity, p, open)).ToList()))
            .ToList();
        var related = relations is null || open is null
            ? []
            : RelationSpec.For(entity.Kind)
                .Where(r => session.IsSupported(r.Kind, r.Key))
                .Select(r => (EditorTabViewModel)new RelatedListViewModel(session, relations, r, entity.Id, open));
        Tabs = [.. Groups, .. related];
        SelectedGroup = Tabs.FirstOrDefault();
        RefreshHeader();
    }

    public void Reload()
    {
        foreach (var g in Groups)
            foreach (var e in g.Editors) e.Load();
        RefreshHeader();
    }

    private void RefreshHeader()
    {
        Title = _session.TryRead(Entity, PropertyCatalog.DisplayName) as string ?? Entity.ToString();
        Subtitle = Entity.Kind switch
        {
            EntityKind.Player or EntityKind.Staff => string.Join("  ·  ", new[]
            {
                Fmt("Age") is { Length: > 0 } age ? $"{age} years" : "",
                Fmt("Contract.Club"),
                Fmt("Nation"),
                Entity.Kind == EntityKind.Staff ? Fmt("Role") : "",
            }.Where(s => s.Length > 0 && s != "—")),
            EntityKind.Club => string.Join("  ·  ", new[] { Fmt("League"), Fmt("Nation") }.Where(s => s.Length > 0 && s != "—")),
            _ => Fmt("Nation"),
        };
        WatchState = _session.Watcher.ModeOf(Entity) switch
        {
            WatchMode.Freeze => "Attributes frozen",
            WatchMode.OnlyUp => "Attributes can only go up",
            _ => null,
        };
        OnPropertyChanged(nameof(IsFrozen));
        OnPropertyChanged(nameof(IsOnlyUp));
    }

    private string Fmt(string key) =>
        _session.IsSupported(Entity.Kind, key) && PropertyCatalog.Find(Entity.Kind, key) is not null ? _session.Format(Entity, key) : "";
}

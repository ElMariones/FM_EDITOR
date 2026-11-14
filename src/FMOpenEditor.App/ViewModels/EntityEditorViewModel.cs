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
    private readonly Action<Entity>? _transfer;

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
    /// <param name="transfer">Opens the Transfer window for a player; no Transfer button without it.</param>
    /// <param name="status">Shows a message in the status bar (copy and paste stats report there).</param>
    public EntityEditorViewModel(EditorSession session, Entity entity, RelationIndex? relations, Action<Entity>? open,
        Action<Entity>? transfer = null, Action<string>? status = null)
    {
        _session = session;
        _transfer = transfer;
        _status = status;
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

    /// <summary>Shows the Transfer button: players, when this game version can move them.</summary>
    public bool CanTransfer => _transfer is not null && Entity.Kind == EntityKind.Player && _session.Transfers.CanTransfer;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void Transfer()
    {
        if (CanTransfer) _transfer!(Entity);
    }

    private readonly Action<string>? _status;

    /// <summary>Shows Copy stats and Paste stats: players only.</summary>
    public bool IsPlayer => Entity.Kind == EntityKind.Player;

    public bool CanPasteStats => IsPlayer && _session.CopiedStats is not null;

    public string PasteStatsTip => _session.CopiedStats is { } c
        ? $"Paste {c.From}'s CA, PA, attributes, positions and feet onto this player"
        : "Paste stats (copy a player's stats first)";

    /// <summary>Called when stats were copied anywhere, so the Paste button wakes up.</summary>
    public void NotifyCopiedStats()
    {
        OnPropertyChanged(nameof(CanPasteStats));
        OnPropertyChanged(nameof(PasteStatsTip));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void CopyStats()
    {
        if (!IsPlayer) return;
        _session.CopiedStats = StatsCopy.Copy(_session, Entity);
        NotifyCopiedStats();
        _status?.Invoke($"Copied {_session.CopiedStats.From}'s stats ({_session.CopiedStats.Values.Count} values). Open another player and press Paste stats.");
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void PasteStats()
    {
        if (!CanPasteStats) return;
        var stats = _session.CopiedStats!;
        var n = StatsCopy.Paste(_session, Entity, stats);
        Reload();
        _status?.Invoke(n == 0
            ? $"{Title} already has {stats.From}'s stats."
            : $"Pasted {stats.From}'s stats onto {Title}: {n} value(s) changed, ready to save.");
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

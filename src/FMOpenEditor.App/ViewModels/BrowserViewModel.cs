using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Presets;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.App.ViewModels;

public sealed record PresetOpOption(PresetOp Op, string Label)
{
    public static readonly IReadOnlyList<PresetOpOption> All =
    [
        new(PresetOp.Set, "Set to"), new(PresetOp.Add, "Add"), new(PresetOp.Multiply, "Multiply by"),
        new(PresetOp.AtLeast, "Raise to at least"), new(PresetOp.AtMost, "Lower to at most"),
    ];

    public override string ToString() => Label;
}

/// <summary>A list of one entity kind with search, presets, mass edit and the detail editor.</summary>
public sealed partial class BrowserViewModel : ObservableObject
{
    private readonly EditorSession _session;
    private readonly Action<string> _status;
    private readonly Action<Entity, Entity> _compare;
    private List<EntityRow> _selection = [];
    private bool _refreshQueued;
    private string? _sortKey;
    private bool _sortDescending;

    public EntityKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<ColumnDef> Columns { get; }
    public ObservableCollection<EntityRow> Rows { get; } = [];
    public ObservableCollection<FilterRowViewModel> Filters { get; } = [];
    public IReadOnlyList<PropertyDef> FilterProperties { get; }
    public IReadOnlyList<PropertyDef> EditableProperties { get; }
    public IReadOnlyList<Preset> QuickPresets { get; }
    public IReadOnlyList<Preset> OtherPresets { get; }
    public bool HasOtherPresets => OtherPresets.Count > 0;
    public IReadOnlyList<PresetOpOption> MassOps => PresetOpOption.All;
    public bool IsPeople => Kind is EntityKind.Player or EntityKind.Staff;

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private EntityEditorViewModel? _editor;
    [ObservableProperty] private bool _showFilters;
    [ObservableProperty] private bool _showMassEdit;
    [ObservableProperty] private int _selectedCount;

    [ObservableProperty] private PropertyDef? _massProperty;
    [ObservableProperty] private PresetOpOption _massOp = PresetOpOption.All[0];
    [ObservableProperty] private string? _massValue;

    public BrowserViewModel(EditorSession session, EntityKind kind, PresetLibrary presets,
        Action<string> status, Action<Entity, Entity> compare)
    {
        _session = session;
        _status = status;
        _compare = compare;
        Kind = kind;
        Title = kind switch
        {
            EntityKind.Staff => "Staff",
            EntityKind.Competition => "Competitions",
            _ => kind + "s",
        };

        Columns = DefaultColumns(kind).Where(c => session.IsSupported(kind, c.Key)).ToList();
        FilterProperties = PropertyCatalog.For(kind).Where(p => session.IsSupported(kind, p.Key)).ToList();
        EditableProperties = PropertyCatalog.For(kind).Where(p => session.CanEdit(kind, p.Key)).ToList();
        _massProperty = EditableProperties.FirstOrDefault();

        var forKind = presets.Presets.Where(p => p.Target == kind).ToList();
        QuickPresets = forKind.Where(p => p.Quick).ToList();
        OtherPresets = forKind.Where(p => !p.Quick).ToList();

        session.ValuesChanged += QueueRefresh;
        Search();
    }

    private static IEnumerable<ColumnDef> DefaultColumns(EntityKind kind) => kind switch
    {
        EntityKind.Player =>
        [
            new(PropertyCatalog.DisplayName, "Name", 180), new("Age", "Age", 62), new("Contract.Club", "Club", 150),
            new("Nation", "Nation", 100), new("CurrentAbility", "CA", 58), new("PotentialAbility", "PA", 58),
            new("Value", "Value", 85), new("Contract.Wage", "Wage", 80), new("Contract.Expiry", "Expires", 100),
        ],
        EntityKind.Staff =>
        [
            new(PropertyCatalog.DisplayName, "Name", 180), new("Age", "Age", 62), new("Role", "Role", 150),
            new("Contract.Club", "Club", 150), new("Nation", "Nation", 100),
            new("CurrentAbility", "CA", 58), new("PotentialAbility", "PA", 58),
        ],
        EntityKind.Club =>
        [
            new(PropertyCatalog.DisplayName, "Name", 180), new("League", "League", 140), new("Nation", "Nation", 95),
            new("Reputation", "Rep", 70), new("Finance.Balance", "Balance", 95), new("Finance.TransferBudget", "Transfer", 95),
            new("Standing.Position", "Pos", 60), new("Standing.Points", "Pts", 60),
        ],
        EntityKind.Competition =>
        [
            new(PropertyCatalog.DisplayName, "Name", 220), new("Nation", "Nation", 120),
            new("Reputation", "Reputation", 90), new("PrizeMoney", "Prize money", 110),
        ],
        _ =>
        [
            new(PropertyCatalog.DisplayName, "Name", 200), new("Continent", "Continent", 170),
            new("Reputation", "Reputation", 90), new("Rules.EuFreeMovement", "EU free movement", 130),
        ],
    };

    public IReadOnlyList<Entity> SelectedEntities => _selection.Select(r => r.Entity).ToList();

    /// <summary>Called by the view when grid selection changes.</summary>
    public void SetSelection(IEnumerable<EntityRow> rows)
    {
        _selection = rows.ToList();
        SelectedCount = _selection.Count;
        var first = _selection.FirstOrDefault();
        if (first is null) return;
        if (Editor?.Entity != first.Entity) Editor = new EntityEditorViewModel(_session, first.Entity);
    }

    partial void OnSearchTextChanged(string? value) => Search();

    [RelayCommand]
    private void Search()
    {
        List<Filter> filters;
        try
        {
            filters = Filters.Select(f => f.ToFilter()).OfType<Filter>().ToList();
        }
        catch (FormatException ex)
        {
            _status(ex.Message);
            return;
        }

        var query = new SearchQuery { Kind = Kind, Text = SearchText, Filters = filters };
        var found = SearchEngine.Run(_session, query);
        var rows = found.Select(e => new EntityRow(e)).ToList();
        foreach (var r in rows) r.Refresh(_session, Columns);
        if (_sortKey is not null) rows = Sort(rows, _sortKey, _sortDescending);

        Rows.Clear();
        foreach (var r in rows) Rows.Add(r);

        var total = _session.GetAll(Kind).Count;
        Summary = found.Count >= query.Limit
            ? $"Showing first {found.Count:N0} of {total:N0} — narrow your search"
            : found.Count == total ? $"{total:N0} {Title.ToLowerInvariant()}" : $"{found.Count:N0} of {total:N0} {Title.ToLowerInvariant()}";
    }

    public void SortBy(string key)
    {
        _sortDescending = _sortKey == key ? !_sortDescending : PropertyCatalog.Get(Kind, key).IsNumeric;
        _sortKey = key;
        var sorted = Sort(Rows, key, _sortDescending);
        Rows.Clear();
        foreach (var r in sorted) Rows.Add(r);
    }

    private static List<EntityRow> Sort(IEnumerable<EntityRow> rows, string key, bool descending)
    {
        var list = rows.ToList();
        list.Sort((a, b) => Values.Compare(a.Raw(key), b.Raw(key)) * (descending ? -1 : 1));
        return list;
    }

    private void QueueRefresh()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            foreach (var r in Rows) r.Refresh(_session, Columns);
            Editor?.Reload();
        }, DispatcherPriority.Background);
    }

    [RelayCommand]
    private void AddFilter()
    {
        Filters.Add(new FilterRowViewModel(_session, FilterProperties));
        ShowFilters = true;
    }

    [RelayCommand]
    private void RemoveFilter(FilterRowViewModel row)
    {
        Filters.Remove(row);
        Search();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Clear();
        SearchText = null;
        Search();
    }

    [RelayCommand]
    private void ToggleFilters()
    {
        ShowFilters = !ShowFilters;
        if (ShowFilters && Filters.Count == 0) AddFilter();
    }

    [RelayCommand] private void ToggleMassEdit() => ShowMassEdit = !ShowMassEdit;

    [RelayCommand]
    private void ApplyPreset(Preset preset)
    {
        var targets = SelectedEntities;
        if (preset.Scope == PresetScope.Selection && targets.Count == 0)
        {
            _status($"Select at least one row to apply {preset.Name}.");
            return;
        }
        var r = PresetEngine.Apply(_session, preset, targets);
        _status(Describe(preset.Name, r));
    }

    [RelayCommand]
    private void ApplyMassEdit()
    {
        if (MassProperty is null) return;
        var targets = SelectedEntities;
        if (targets.Count == 0)
        {
            _status("Select the rows to change first (Ctrl+A selects all results).");
            return;
        }
        try
        {
            object? value = MassProperty.Kind == ValueKind.Money && MassOp.Op != PresetOp.Multiply && MassValue is not null
                ? ValueParser.ParseMoney(MassValue)
                : MassOp.Op is PresetOp.Add or PresetOp.Multiply
                    ? MassValue
                    : ValueParser.Parse(_session, MassProperty, MassValue);
            var r = PresetEngine.MassEdit(_session, targets, MassProperty.Key, MassOp.Op, value);
            _status(Describe($"Mass edit of {MassProperty.Label}", r));
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or NotSupportedException)
        {
            _status(ex.Message);
        }
    }

    [RelayCommand]
    private void Compare()
    {
        if (_selection.Count != 2)
        {
            _status("Select exactly two rows to compare.");
            return;
        }
        _compare(_selection[0].Entity, _selection[1].Entity);
    }

    [RelayCommand]
    private void ReleaseOnFree()
    {
        var n = 0;
        var action = CustomActions.Find("ReleaseOnFree")!;
        foreach (var e in SelectedEntities) n += action.Apply(_session, e, null) > 0 ? 1 : 0;
        _status(n > 0 ? $"Released {n} on a free transfer. Save changes to apply in the game."
                      : "Nothing to release (free agents, or contracts not supported for this version).");
    }

    [RelayCommand] private void Freeze() => Watch(WatchMode.Freeze);
    [RelayCommand] private void OnlyUp() => Watch(WatchMode.OnlyUp);

    private void Watch(WatchMode mode)
    {
        var n = 0;
        foreach (var e in SelectedEntities) n += _session.Watcher.WatchAttributes(e, mode) > 0 ? 1 : 0;
        _status(mode == WatchMode.Freeze ? $"Froze the attributes of {n}." : $"{n} can now only improve.");
        Editor?.Reload();
    }

    [RelayCommand]
    private void Unwatch()
    {
        foreach (var e in SelectedEntities) _session.Watcher.Unwatch(e);
        _status("Watch removed.");
        Editor?.Reload();
    }

    private static string Describe(string what, PresetResult r)
    {
        var s = r.Changes == 0 ? $"{what}: nothing to change." : $"{what}: {r.Changes} change(s) to {r.Entities} — ready to save.";
        if (r.Skipped.Count > 0) s += $" Not supported in this version: {string.Join(", ", r.Skipped.Take(4))}{(r.Skipped.Count > 4 ? "…" : "")}.";
        return s;
    }

    public void Detach() => _session.ValuesChanged -= QueueRefresh;
}

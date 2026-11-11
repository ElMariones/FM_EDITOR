using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

/// <summary>
/// Reverse lookups over reference properties: "every player whose club is X". Each (kind, key)
/// is indexed with one pass over that kind the first time it is asked for, then answered from
/// memory until any value changes (an edit, a save, a re-read).
/// </summary>
public sealed class RelationIndex
{
    private readonly EditorSession _session;
    private readonly Dictionary<(EntityKind Kind, string Key), ILookup<int, Entity>> _lookups = [];

    public RelationIndex(EditorSession session)
    {
        _session = session;
        session.ValuesChanged += _lookups.Clear;
    }

    /// <summary>Every <paramref name="kind"/> whose <paramref name="key"/> points at <paramref name="id"/>.</summary>
    public IEnumerable<Entity> Find(EntityKind kind, string key, int id)
    {
        if (!_session.IsSupported(kind, key)) return [];
        if (!_lookups.TryGetValue((kind, key), out var lookup))
        {
            lookup = _session.GetAll(kind)
                .Select(e => (Entity: e, Ref: _session.TryRead(e, key) as int?))
                .Where(x => x.Ref is not null)
                .ToLookup(x => x.Ref!.Value, x => x.Entity);
            _lookups[(kind, key)] = lookup;
        }
        return lookup[id];
    }
}

/// <summary>One "who points here" list shown as a tab in the editor, e.g. a club's players.</summary>
public sealed record RelationSpec(string Title, EntityKind Kind, string Key, string? SortKey, string[] DetailKeys)
{
    public static IReadOnlyList<RelationSpec> For(EntityKind kind) => kind switch
    {
        EntityKind.Club =>
        [
            new("Players", EntityKind.Player, "Contract.Club", "CurrentAbility", ["Age", "Nation", "CurrentAbility"]),
            new("Staff", EntityKind.Staff, "Contract.Club", "CurrentAbility", ["Role", "Age", "Nation"]),
        ],
        EntityKind.Competition =>
        [
            new("Clubs", EntityKind.Club, "League", "Reputation", ["Nation", "Reputation"]),
        ],
        EntityKind.Nation =>
        [
            new("Players", EntityKind.Player, "Nation", "CurrentAbility", ["Age", "Contract.Club", "CurrentAbility"]),
            new("Clubs", EntityKind.Club, "Nation", "Reputation", ["League", "Reputation"]),
            new("Competitions", EntityKind.Competition, "Nation", "Reputation", ["Reputation"]),
        ],
        _ => [],
    };
}

/// <summary>A tab of the detail editor: either a group of fields or a list of related entities.</summary>
public abstract class EditorTabViewModel(string name)
{
    public string Name { get; } = name;
}

/// <summary>A row in a related list: picture, name, a short line of details. Clicking it opens that entity.</summary>
public sealed class RelatedItem
{
    public const string PictureKey = "pic";

    public RelatedItem(Entity entity, string name, string detail, string? badge)
    {
        Entity = entity;
        Name = name;
        Detail = detail;
        Badge = badge;
        Images.Target(PictureKey, entity.Kind, entity.Id);
    }

    public Entity Entity { get; }
    public string Name { get; }
    public string Detail { get; }

    /// <summary>The number on the right (CA, reputation), if there is one.</summary>
    public string? Badge { get; }
    public bool HasBadge => !string.IsNullOrEmpty(Badge);

    public RowImages Images { get; } = new();
}

/// <summary>
/// A related list tab. Its rows are worked out the first time the tab is shown, so opening a
/// nation doesn't scan every player until the user actually asks for its players.
/// </summary>
public sealed class RelatedListViewModel : EditorTabViewModel
{
    private readonly EditorSession _session;
    private readonly RelationIndex _index;
    private readonly RelationSpec _spec;
    private readonly int _targetId;
    private readonly Action<Entity> _open;
    private IReadOnlyList<RelatedItem>? _items;

    public RelatedListViewModel(EditorSession session, RelationIndex index, RelationSpec spec, int targetId, Action<Entity> open)
        : base(spec.Title)
    {
        _session = session;
        _index = index;
        _spec = spec;
        _targetId = targetId;
        _open = open;
    }

    public IReadOnlyList<RelatedItem> Items => _items ??= Build();
    public bool IsEmpty => Items.Count == 0;
    public string Summary => Items.Count == 1 ? $"1 {Singular}" : $"{Items.Count:N0} {Name.ToLowerInvariant()}";
    private string Singular => Name.TrimEnd('s').ToLowerInvariant();

    public CommunityToolkit.Mvvm.Input.IRelayCommand<RelatedItem> OpenCommand =>
        _openCommand ??= new CommunityToolkit.Mvvm.Input.RelayCommand<RelatedItem>(item => { if (item is not null) _open(item.Entity); });
    private CommunityToolkit.Mvvm.Input.IRelayCommand<RelatedItem>? _openCommand;

    private string Detail(Entity e, string key)
    {
        var text = _session.Format(e, key);
        return key == "Age" && text.Length > 0 && text != "—" ? $"{text} years" : text;
    }

    private List<RelatedItem> Build()
    {
        var found = _index.Find(_spec.Kind, _spec.Key, _targetId).ToList();
        var sortable = _spec.SortKey is { } k && _session.IsSupported(_spec.Kind, k);
        var details = _spec.DetailKeys
            .Where(d => d != _spec.SortKey && _session.IsSupported(_spec.Kind, d) && d != _spec.Key)
            .ToList();

        var rows = found.Select(e =>
        {
            var name = _session.TryRead(e, PropertyCatalog.DisplayName) as string ?? e.ToString();
            var detail = string.Join("  ·  ", details.Select(d => Detail(e, d)).Where(s => s.Length > 0 && s != "—"));
            var sortValue = sortable ? _session.TryRead(e, _spec.SortKey!) : null;
            return (Item: new RelatedItem(e, name, detail, sortable ? _session.Format(e, _spec.SortKey!) : null), Sort: sortValue);
        }).ToList();

        rows.Sort((a, b) => sortable
            ? -Values.Compare(a.Sort, b.Sort)
            : string.Compare(a.Item.Name, b.Item.Name, StringComparison.CurrentCultureIgnoreCase));
        return rows.Select(r => r.Item).ToList();
    }
}

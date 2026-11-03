using System.ComponentModel;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

public sealed record ColumnDef(string Key, string Header, double Width);

/// <summary>A grid row. Cells are exposed through a string indexer so columns can be built per entity kind.</summary>
public sealed class EntityRow(Entity entity) : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _text = [];
    private readonly Dictionary<string, object?> _raw = [];

    public Entity Entity { get; } = entity;
    public bool HasPending { get; private set; }

    public string this[string key] => _text.GetValueOrDefault(key) ?? "";
    public object? Raw(string key) => _raw.GetValueOrDefault(key);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Refresh(EditorSession session, IEnumerable<ColumnDef> columns)
    {
        foreach (var c in columns)
        {
            var def = PropertyCatalog.Get(Entity.Kind, c.Key);
            var v = session.TryRead(Entity, c.Key);
            _raw[c.Key] = def.Kind == ValueKind.Reference && v is int id && def.RefKind is { } k ? session.NameOf(k, id) : v;
            _text[c.Key] = Values.Format(def, v, session.NameOf);
        }
        HasPending = session.Changes.Pending.Any(p => p.Entity == Entity);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPending)));
    }
}

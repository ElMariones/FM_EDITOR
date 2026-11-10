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

    /// <summary>Faces and badges per column; see <see cref="RowImages"/>.</summary>
    public RowImages Images { get; } = new();

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

            if (c.Key == PropertyCatalog.DisplayName) Images.Target(c.Key, Entity.Kind, Entity.Id);
            else if (def.Kind == ValueKind.Reference) Images.Target(c.Key, v is int refId ? def.RefKind : null, v as int? ?? 0);
        }
        HasPending = session.Changes.Pending.Any(p => p.Entity == Entity);
        // Avalonia's indexer bindings refresh on the indexer property's CLR name, "Item".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPending)));
    }
}

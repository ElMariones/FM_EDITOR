using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

/// <summary>
/// The editor's view of a database: staged edits layered over the source, derived properties,
/// and freeze / only-up watches. UI, presets, search and mass edit all go through this.
/// </summary>
public sealed class EditorSession : IDisposable
{
    private readonly Dictionary<(EntityKind, int), string> _names = [];

    public IGameDatabase Database { get; }
    public ChangeSet Changes { get; } = new();
    public AttributeWatcher Watcher { get; }

    /// <summary>When true, edits are written to the game immediately instead of being staged.</summary>
    public bool AutoSave { get; set; }

    /// <summary>Raised after any value changes (staged, saved, undone or written by a watch).</summary>
    public event Action? ValuesChanged;

    public EditorSession(IGameDatabase database)
    {
        Database = database;
        Watcher = new AttributeWatcher(this);
        Changes.Changed += () => ValuesChanged?.Invoke();
    }

    public DateOnly GameDate => Database.GameDate;

    public IReadOnlyList<Entity> GetAll(EntityKind kind) => Database.GetAll(kind);

    public bool IsSupported(EntityKind kind, string key)
    {
        var def = PropertyCatalog.Find(kind, key);
        if (def is null) return false;
        if (!def.IsDerived) return Database.Supports(kind, key);
        return key switch
        {
            "Age" => Database.Supports(kind, "DateOfBirth"),
            PropertyCatalog.DisplayName => Database.Supports(kind, "Name") || Database.Supports(kind, "LastName"),
            _ => false,
        };
    }

    public bool CanEdit(EntityKind kind, string key) =>
        PropertyCatalog.Find(kind, key) is { IsDerived: false } && Database.Supports(kind, key);

    public object? Read(Entity entity, string key)
    {
        if (Changes.TryGet(entity, key, out var staged)) return staged;
        var def = PropertyCatalog.Get(entity.Kind, key);
        if (def.IsDerived) return ReadDerived(entity, key);
        return Database.Supports(entity.Kind, key) ? Database.Read(entity, key) : null;
    }

    /// <summary>Reads, returning null instead of throwing when memory can't be read.</summary>
    public object? TryRead(Entity entity, string key)
    {
        try { return Read(entity, key); }
        catch (Exception) { return null; }
    }

    private object? ReadDerived(Entity e, string key)
    {
        switch (key)
        {
            case "Age":
                if (TryRead(e, "DateOfBirth") is not DateOnly dob) return null;
                var age = GameDate.Year - dob.Year;
                if (GameDate < dob.AddYears(age)) age--;
                return age;
            case PropertyCatalog.DisplayName:
                if (e.Kind is EntityKind.Player or EntityKind.Staff)
                {
                    if (e.Kind == EntityKind.Player && TryRead(e, "CommonName") is string { Length: > 0 } common) return common;
                    return $"{TryRead(e, "FirstName")} {TryRead(e, "LastName")}".Trim();
                }
                return TryRead(e, "Name") as string ?? "";
            default:
                return null;
        }
    }

    public string NameOf(EntityKind kind, int id)
    {
        if (_names.TryGetValue((kind, id), out var cached)) return cached;
        var e = Database.Find(kind, id);
        var name = e is null ? $"#{id}" : TryRead(e, PropertyCatalog.DisplayName) as string ?? $"#{id}";
        _names[(kind, id)] = name;
        return name;
    }

    public string Format(Entity entity, string key)
    {
        var def = PropertyCatalog.Get(entity.Kind, key);
        return Values.Format(def, TryRead(entity, key), NameOf);
    }

    /// <summary>Sets a value: staged by default, or written straight away when <see cref="AutoSave"/> is on.</summary>
    public void Set(Entity entity, string key, object? value)
    {
        var def = PropertyCatalog.Get(entity.Kind, key);
        if (def.IsDerived) throw new InvalidOperationException($"{def.Label} is calculated and can't be edited.");
        if (!Database.Supports(entity.Kind, key))
            throw new NotSupportedException($"{def.Label} isn't supported for this game version yet.");

        var coerced = Values.Coerce(def, value);
        if (key is "Name" or "FirstName" or "LastName" or "CommonName") _names.Remove((entity.Kind, entity.Id));

        if (AutoSave)
        {
            var original = Database.Read(entity, key);
            Changes.Stage(entity, key, original, coerced);
            Save();
        }
        else
        {
            var original = Changes.IsPending(entity, key) ? null : Database.Read(entity, key);
            Changes.Stage(entity, key, original, coerced);
        }
        Watcher.OnEdited(entity, key, coerced);
    }

    public IReadOnlyList<(PendingChange Change, Exception Error)> Save()
    {
        var failed = Changes.Commit(Database);
        _names.Clear();
        return failed;
    }

    public int UndoLastSave()
    {
        var n = Changes.UndoLastCommit(Database);
        _names.Clear();
        return n;
    }

    public void Discard()
    {
        Changes.Discard();
        _names.Clear();
    }

    public void Refresh()
    {
        Database.Refresh();
        _names.Clear();
        ValuesChanged?.Invoke();
    }

    internal void RaiseValuesChanged() => ValuesChanged?.Invoke();

    public void Dispose() => Database.Dispose();
}

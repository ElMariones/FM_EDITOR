using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

public enum WatchMode
{
    /// <summary>Any change the game makes is reverted.</summary>
    Freeze,
    /// <summary>Increases are kept, decreases are reverted.</summary>
    OnlyUp,
}

public sealed record Watch(Entity Entity, string Key, WatchMode Mode)
{
    public object? Value { get; set; }
}

/// <summary>
/// Keeps chosen values from going down (or changing at all) while the game runs. Call
/// <see cref="Tick"/> periodically; it writes directly to the database, bypassing staging,
/// because the point is to undo what the game did.
/// </summary>
public sealed class AttributeWatcher(EditorSession session)
{
    private readonly Dictionary<(Entity, string), Watch> _watches = [];

    public IReadOnlyCollection<Watch> Watches => _watches.Values;
    public event Action? Changed;

    public bool IsWatched(Entity entity) => _watches.Keys.Any(k => k.Item1 == entity);

    public WatchMode? ModeOf(Entity entity) =>
        _watches.Values.FirstOrDefault(w => w.Entity == entity)?.Mode;

    /// <summary>Watches every attribute of <paramref name="entity"/> that the game version supports.</summary>
    public int WatchAttributes(Entity entity, WatchMode mode)
    {
        var count = 0;
        foreach (var p in PropertyCatalog.For(entity.Kind).Where(p => p.IsAttribute && session.CanEdit(entity.Kind, p.Key)))
        {
            _watches[(entity, p.Key)] = new Watch(entity, p.Key, mode) { Value = session.TryRead(entity, p.Key) };
            count++;
        }
        Changed?.Invoke();
        return count;
    }

    public void Watch(Entity entity, string key, WatchMode mode)
    {
        _watches[(entity, key)] = new Watch(entity, key, mode) { Value = session.TryRead(entity, key) };
        Changed?.Invoke();
    }

    public void Unwatch(Entity entity)
    {
        foreach (var k in _watches.Keys.Where(k => k.Item1 == entity).ToList()) _watches.Remove(k);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _watches.Clear();
        Changed?.Invoke();
    }

    /// <summary>An edit by the user becomes the new value to hold.</summary>
    internal void OnEdited(Entity entity, string key, object? value)
    {
        if (_watches.TryGetValue((entity, key), out var w)) w.Value = value;
    }

    /// <summary>Checks every watch against the game. Returns how many values were restored.</summary>
    public int Tick()
    {
        var restored = 0;
        foreach (var w in _watches.Values)
        {
            object? now;
            try { now = session.Database.Read(w.Entity, w.Key); }
            catch (Exception) { continue; }

            var cmp = Values.Compare(now, w.Value);
            if (cmp == 0) continue;
            if (w.Mode == WatchMode.OnlyUp && cmp > 0)
            {
                w.Value = now;
                continue;
            }

            // A staged edit wins over the watch; the user's latest intent is what we hold.
            if (session.Changes.IsPending(w.Entity, w.Key)) continue;
            try
            {
                session.Database.Write(w.Entity, w.Key, w.Value);
                restored++;
            }
            catch (Exception) { /* entity may be gone (retired, deleted); keep trying next tick */ }
        }
        if (restored > 0) session.RaiseValuesChanged();
        return restored;
    }
}

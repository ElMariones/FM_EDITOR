using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Editing;

public sealed record PendingChange(Entity Entity, string Key, object? Original, object? Value);

/// <summary>
/// Edits waiting to be written to the game, plus a history of saved batches so a save can be undone.
/// </summary>
public sealed class ChangeSet
{
    private readonly Dictionary<(Entity, string), PendingChange> _pending = [];
    private readonly Stack<IReadOnlyList<PendingChange>> _history = new();

    public IReadOnlyCollection<PendingChange> Pending => _pending.Values;
    public int Count => _pending.Count;
    public bool CanUndo => _history.Count > 0;

    public event Action? Changed;

    public bool TryGet(Entity entity, string key, out object? value)
    {
        if (_pending.TryGetValue((entity, key), out var change))
        {
            value = change.Value;
            return true;
        }
        value = null;
        return false;
    }

    public bool IsPending(Entity entity, string key) => _pending.ContainsKey((entity, key));

    /// <summary>Stages a value. Staging the original value back removes the pending change.</summary>
    public void Stage(Entity entity, string key, object? original, object? value)
    {
        if (_pending.TryGetValue((entity, key), out var existing)) original = existing.Original;

        if (Values.AreEqual(original, value)) _pending.Remove((entity, key));
        else _pending[(entity, key)] = new PendingChange(entity, key, original, value);
        Changed?.Invoke();
    }

    /// <summary>Writes every pending change. Changes that fail stay pending and are returned.</summary>
    public IReadOnlyList<(PendingChange Change, Exception Error)> Commit(IGameDatabase db)
    {
        var done = new List<PendingChange>();
        var failed = new List<(PendingChange, Exception)>();
        foreach (var change in _pending.Values.ToList())
        {
            try
            {
                db.Write(change.Entity, change.Key, change.Value);
                done.Add(change);
                _pending.Remove((change.Entity, change.Key));
            }
            catch (Exception ex)
            {
                failed.Add((change, ex));
            }
        }
        if (done.Count > 0) _history.Push(done);
        Changed?.Invoke();
        return failed;
    }

    /// <summary>Writes the original values of the last saved batch back.</summary>
    public int UndoLastCommit(IGameDatabase db)
    {
        if (!_history.TryPop(out var batch)) return 0;
        foreach (var change in batch) db.Write(change.Entity, change.Key, change.Original);
        Changed?.Invoke();
        return batch.Count;
    }

    public void Discard()
    {
        _pending.Clear();
        Changed?.Invoke();
    }
}

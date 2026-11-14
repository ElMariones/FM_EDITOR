using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Data;

public sealed partial class LiveDatabase
{
    private List<MemoryPatch>? _recording;

    /// <summary>Every write to the game goes through here, so it can be recorded and refused when stale.</summary>
    private void Put(nint address, ReadOnlySpan<byte> data)
    {
        if (IsStale) throw new StaleDataException();
        byte[]? before = null;
        if (_recording is not null) before = _memory.ReadBytes(address, data.Length);
        _memory.WriteChecked(address, data);
        _recording?.Add(new MemoryPatch(address, before!, data.ToArray()));
    }

    public void StartRecording() => _recording = [];

    public IReadOnlyList<MemoryPatch> StopRecording()
    {
        var list = _recording ?? [];
        _recording = null;
        return list;
    }

    public void Revert(IReadOnlyList<MemoryPatch> patches)
    {
        if (IsStale) throw new StaleDataException();
        // Check everything first, as the undo would leave memory: walking back, each patch must find
        // what it wrote, either in the game or in what a later patch's undo puts back.
        var overlay = new Dictionary<nint, byte>();
        var changed = 0;
        for (var i = patches.Count - 1; i >= 0; i--)
        {
            var p = patches[i];
            var now = _memory.ReadBytes(p.Address, p.After.Length);
            for (var j = 0; j < now.Length; j++)
                if (overlay.TryGetValue(p.Address + j, out var b)) now[j] = b;
            if (!now.AsSpan().SequenceEqual(p.After)) changed++;
            for (var j = 0; j < p.Before.Length; j++) overlay[p.Address + j] = p.Before[j];
        }
        if (changed > 0) throw new UndoRefusedException(changed);
        for (var i = patches.Count - 1; i >= 0; i--) _memory.WriteChecked(patches[i].Address, patches[i].Before);
    }

    // ---- noticing a reload ----

    private readonly List<(nint Address, long Word)> _fingerprint = [];

    /// <summary>True once the records the editor read are no longer where it read them.</summary>
    public bool IsStale { get; private set; }

    /// <summary>
    /// Remembers a handful of records (the first qword of each: a vtable or a pointer) to compare later.
    /// Called after every <see cref="Refresh"/>.
    /// </summary>
    private void TakeFingerprint()
    {
        _fingerprint.Clear();
        IsStale = false;
        foreach (var kind in new[] { EntityKind.Player, EntityKind.Club, EntityKind.Staff })
        {
            var list = GetAll(kind);
            if (list.Count == 0) continue;
            var step = Math.Max(1, list.Count / 8);
            for (var i = 0; i < list.Count && _fingerprint.Count < 24; i += step)
            {
                var a = list[i].Address;
                if (_memory.Read(a, new byte[8]) is false) continue;
                _fingerprint.Add((a, _memory.ReadInt64(a)));
            }
        }
    }

    /// <summary>
    /// Re-reads the fingerprint. When most of those records have gone or changed, the game has loaded a
    /// save or left it, and every write is refused until <see cref="Refresh"/>. Returns <see cref="IsStale"/>.
    /// </summary>
    public bool CheckStale()
    {
        if (IsStale || _fingerprint.Count == 0) return IsStale;
        var gone = 0;
        var buf = new byte[8];
        foreach (var (a, w) in _fingerprint)
            if (!_memory.Read(a, buf) || BitConverter.ToInt64(buf) != w) gone++;
        if (gone * 2 > _fingerprint.Count) IsStale = true;
        return IsStale;
    }
}

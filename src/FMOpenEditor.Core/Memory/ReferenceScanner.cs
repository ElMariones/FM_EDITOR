using System.Buffers.Binary;
using System.Text;

namespace FMOpenEditor.Core.Memory;

/// <summary>A pointer found in memory: the 8 bytes at <see cref="Address"/> hold <c>target - Offset</c>.</summary>
public readonly record struct PointerHit(nint Address, nint Value, long Offset);

/// <summary>
/// Searches for strings and for pointers. Strings find a record by a name you can see in the game;
/// pointers answer "what points here?", which is how you walk from a record back up to a global root.
/// </summary>
public static class ReferenceScanner
{
    /// <summary>Every address where <paramref name="text"/> is stored in <paramref name="encoding"/> (exact match).</summary>
    public static List<nint> FindString(IMemoryAccess memory, string text, Encoding encoding,
        int maxResults = 10_000, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Enter some text to search for.", nameof(text));
        var needle = encoding.GetBytes(text);
        return FindBytes(memory, needle, memory.Regions(), maxResults, ct);
    }

    public static List<nint> FindBytes(IMemoryAccess memory, byte[] needle, IEnumerable<MemoryRegion> regions,
        int maxResults = 10_000, CancellationToken ct = default)
    {
        var results = new List<nint>();
        RegionScan.ForEachChunk(memory, regions, needle.Length, (address, data, owned) =>
        {
            var pos = 0;
            while (pos < owned)
            {
                var i = data[pos..].IndexOf(needle);
                if (i < 0 || pos + i >= owned) break;
                pos += i;
                results.Add(address + pos);
                if (results.Count >= maxResults) return false;
                pos++;
            }
            return true;
        }, ct);
        return results;
    }

    /// <summary>
    /// Every 8-byte-aligned pointer whose value lies in [<paramref name="target"/> - <paramref name="maxOffset"/>,
    /// <paramref name="target"/>]. A nonzero offset means the pointer points at the start of something that
    /// contains the target (a record whose field you found, for example).
    /// </summary>
    public static List<PointerHit> FindPointers(IMemoryAccess memory, nint target, long maxOffset = 0,
        int maxResults = 10_000, CancellationToken ct = default)
    {
        var lo = (long)target - Math.Max(0, maxOffset);
        var hi = (long)target;
        var results = new List<PointerHit>();
        RegionScan.ForEachChunk(memory, memory.Regions(), 0, (address, data, owned) =>
        {
            var start = (int)((8 - (long)address % 8) % 8);
            for (var i = start; i + 8 <= owned; i += 8)
            {
                var v = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(i, 8));
                if (v < lo || v > hi) continue;
                results.Add(new PointerHit(address + i, (nint)v, hi - v));
                if (results.Count >= maxResults) return false;
            }
            return true;
        }, ct);
        results.Sort((a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : a.Address.CompareTo(b.Address));
        return results;
    }
}

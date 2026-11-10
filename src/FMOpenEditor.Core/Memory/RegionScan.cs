namespace FMOpenEditor.Core.Memory;

/// <summary>
/// Visits one chunk of a memory region. <paramref name="data"/> may run past <paramref name="ownedLength"/>
/// (the overlap with the next chunk) so matches that straddle a chunk boundary can still be read; only
/// matches that <em>start</em> before <paramref name="ownedLength"/> belong to this chunk. Return false to stop.
/// </summary>
public delegate bool ChunkVisitor(nint address, ReadOnlySpan<byte> data, int ownedLength);

/// <summary>Walks memory regions in fixed-size, overlapping chunks.</summary>
public static class RegionScan
{
    public const int ChunkSize = 4 * 1024 * 1024;

    /// <returns>False if the visitor stopped the walk early.</returns>
    public static bool ForEachChunk(IMemoryAccess memory, IEnumerable<MemoryRegion> regions, int overlap,
        ChunkVisitor visit, CancellationToken ct = default)
    {
        var buffer = new byte[ChunkSize + Math.Max(0, overlap)];
        foreach (var region in regions)
        {
            for (long off = 0; off < region.Size; off += ChunkSize)
            {
                ct.ThrowIfCancellationRequested();
                var len = (int)Math.Min(buffer.Length, region.Size - off);
                var owned = (int)Math.Min(ChunkSize, region.Size - off);
                var span = buffer.AsSpan(0, len);
                if (!memory.Read(region.Start + (nint)off, span)) continue;
                if (!visit(region.Start + (nint)off, span, owned)) return false;
            }
        }
        return true;
    }

    /// <summary>Regions that don't overlap [<paramref name="start"/>, start+<paramref name="size"/>).</summary>
    public static IEnumerable<MemoryRegion> Outside(this IEnumerable<MemoryRegion> regions, nint start, long size) =>
        regions.Where(r => r.Start + (nint)r.Size <= start || r.Start >= start + (nint)size);
}

using System.Buffers.Binary;
using System.Text;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.Core.Research;

/// <summary>One vtable of a class. A class with multiple inheritance has one per base subobject.</summary>
public sealed record RttiVtable(nint Address, nint CompleteObjectLocator, int ObjectOffset);

/// <summary>A C++ class found through MSVC run-time type information.</summary>
public sealed record RttiClass(string Name, string MangledName, nint TypeDescriptor, IReadOnlyList<RttiVtable> Vtables)
{
    /// <summary>The vtable stored at offset 0 of a complete object: the one to look for in the heap.</summary>
    public RttiVtable? PrimaryVtable => Vtables.FirstOrDefault(v => v.ObjectOffset == 0) ?? Vtables.FirstOrDefault();
}

/// <summary>Heap objects found by their vtable pointer.</summary>
public sealed record InstanceScanResult(IReadOnlyDictionary<nint, int> Counts, IReadOnlyDictionary<nint, List<nint>> Samples);

/// <summary>
/// Finds C++ classes in an MSVC x64 module through RTTI, then finds their objects in the heap.
/// <para>
/// MSVC stores each polymorphic class's name in a TypeDescriptor (<c>.?AVplayer@@</c>). A
/// CompleteObjectLocator refers to the TypeDescriptor by RVA and to itself by RVA, and the 8 bytes just
/// before each vtable point at a CompleteObjectLocator. Every live object of the class starts with a
/// pointer to that vtable, so scanning the heap for that pointer lists every object and gives each
/// one's base address. No root is needed.
/// </para>
/// </summary>
public static class RttiScanner
{
    private const int MaxNameLength = 512;

    public static List<RttiClass> FindClasses(IMemoryAccess memory, nint moduleBase, long moduleSize, CancellationToken ct = default) =>
        FindClasses(ModuleImage.Load(memory, moduleBase, moduleSize, ct), ct);

    public static List<RttiClass> FindClasses(ModuleImage image, CancellationToken ct = default)
    {
        var baseAddr = image.Base;

        // 1. TypeDescriptors: { void* vftable; void* spare; char name[]; }, name starting ".?AV" or ".?AU".
        var typeDescriptors = new Dictionary<uint, (nint Address, string Mangled)>();
        var marker = ".?A"u8.ToArray();
        foreach (var (start, data) in image.Sections)
        {
            var pos = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var i = data.AsSpan(pos).IndexOf(marker);
                if (i < 0) break;
                pos += i;
                var nameAt = pos;
                pos++;
                if (nameAt < 16 || nameAt + 4 >= data.Length || (data[nameAt + 3] != 'V' && data[nameAt + 3] != 'U')) continue;
                if (BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(nameAt - 16, 8)) == 0) continue;
                var name = ReadAsciiZ(data, nameAt);
                if (name is null) continue;
                var td = start + nameAt - 16;
                typeDescriptors[(uint)(td - baseAddr)] = (td, name);
            }
        }
        if (typeDescriptors.Count == 0) return [];

        // 2. CompleteObjectLocators (x64): { u32 signature = 1; u32 offset; u32 cdOffset;
        //    u32 typeDescriptorRva; u32 classDescriptorRva; u32 selfRva; }
        var locators = new Dictionary<nint, (uint TdRva, int Offset)>();
        foreach (var (start, data) in image.Sections)
        {
            ct.ThrowIfCancellationRequested();
            var first = (int)((4 - (long)start % 4) % 4);
            for (var i = first; i + 24 <= data.Length; i += 4)
            {
                if (data[i] != 1 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i, 4)) != 1) continue;
                var col = start + i;
                if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 20, 4)) != (uint)(col - baseAddr)) continue;
                var tdRva = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 12, 4));
                if (!typeDescriptors.ContainsKey(tdRva)) continue;
                locators[col] = (tdRva, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4, 4)));
            }
        }

        // 3. Vtables: the 8 bytes before a vtable hold the address of its CompleteObjectLocator.
        var vtables = new Dictionary<uint, List<RttiVtable>>();
        if (locators.Count > 0)
        {
            foreach (var (start, data) in image.Sections)
            {
                ct.ThrowIfCancellationRequested();
                var first = (int)((8 - (long)start % 8) % 8);
                for (var i = first; i + 8 <= data.Length; i += 8)
                {
                    var v = (nint)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i, 8));
                    if (!locators.TryGetValue(v, out var col)) continue;
                    if (!vtables.TryGetValue(col.TdRva, out var list)) vtables[col.TdRva] = list = [];
                    list.Add(new RttiVtable(start + i + 8, v, col.Offset));
                }
            }
        }

        return typeDescriptors
            .Select(kv => new RttiClass(Demangle(kv.Value.Mangled), kv.Value.Mangled, kv.Value.Address,
                vtables.TryGetValue(kv.Key, out var v) ? v.OrderBy(x => x.ObjectOffset).ToList() : []))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Scans writable memory outside the module for 8-byte-aligned pointers to any of <paramref name="vtables"/>.
    /// Each hit is (almost always) the base address of a live object of that class.
    /// </summary>
    public static InstanceScanResult FindInstances(IMemoryAccess memory, IReadOnlyCollection<nint> vtables,
        nint moduleBase, long moduleSize, int maxSamplesPerVtable = 1000, CancellationToken ct = default)
    {
        var wanted = vtables.Select(v => (long)v).ToHashSet();
        var counts = vtables.Distinct().ToDictionary(v => v, _ => 0);
        var samples = vtables.Distinct().ToDictionary(v => v, _ => new List<nint>());
        if (wanted.Count == 0) return new InstanceScanResult(counts, samples);
        long min = wanted.Min(), max = wanted.Max();

        var regions = memory.Regions().Where(r => r.Writable).Outside(moduleBase, moduleSize);
        RegionScan.ForEachChunk(memory, regions, 0, (address, data, owned) =>
        {
            var first = (int)((8 - (long)address % 8) % 8);
            for (var i = first; i + 8 <= owned; i += 8)
            {
                var v = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(i, 8));
                if (v < min || v > max || !wanted.Contains(v)) continue;
                var key = (nint)v;
                counts[key]++;
                if (samples[key].Count < maxSamplesPerVtable) samples[key].Add(address + i);
            }
            return true;
        }, ct);
        return new InstanceScanResult(counts, samples);
    }

    /// <summary>Turns <c>.?AVplayer@fm@@</c> into <c>fm::player</c>. Templates and other complex names are returned as-is.</summary>
    public static string Demangle(string mangled)
    {
        if (!mangled.StartsWith(".?A", StringComparison.Ordinal) || mangled.Length < 5) return mangled;
        var body = mangled[4..];
        if (body.EndsWith("@@", StringComparison.Ordinal)) body = body[..^2];
        if (body.Contains('?') || body.Contains('$')) return body;
        var parts = body.Split('@', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(parts);
        return string.Join("::", parts);
    }

    private static string? ReadAsciiZ(byte[] data, int start)
    {
        var end = start;
        while (end < data.Length && end - start < MaxNameLength && data[end] != 0)
        {
            if (data[end] < 0x20 || data[end] > 0x7E) return null;
            end++;
        }
        if (end >= data.Length || data[end] != 0 || !"@@"u8.SequenceEqual(data.AsSpan(end - 2, 2))) return null;
        return Encoding.ASCII.GetString(data, start, end - start);
    }
}

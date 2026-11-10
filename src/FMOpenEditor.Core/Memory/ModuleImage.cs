using System.Buffers.Binary;

namespace FMOpenEditor.Core.Memory;

/// <summary>A copy of a loaded module's readable sections, for scans that need random access to it.</summary>
public sealed class ModuleImage
{
    private const int PageSize = 4096;
    private readonly List<(nint Start, byte[] Data)> _sections;

    private ModuleImage(nint moduleBase, long size, List<(nint, byte[])> sections)
    {
        Base = moduleBase;
        Size = size;
        _sections = sections;
    }

    public nint Base { get; }
    public long Size { get; }
    public IReadOnlyList<(nint Start, byte[] Data)> Sections => _sections;

    public bool Contains(nint address) => address >= Base && address < Base + (nint)Size;

    public static ModuleImage Load(IMemoryAccess memory, nint moduleBase, long size, CancellationToken ct = default)
    {
        var end = moduleBase + (nint)size;
        var sections = new List<(nint, byte[])>();
        foreach (var r in memory.Regions())
        {
            ct.ThrowIfCancellationRequested();
            var s = Math.Max((long)r.Start, (long)moduleBase);
            var e = Math.Min((long)r.Start + r.Size, (long)end);
            if (e <= s) continue;
            var data = new byte[e - s];
            if (!memory.Read((nint)s, data))
            {
                // Fall back to page by page; unreadable pages stay zero.
                for (long p = 0; p < data.Length; p += PageSize)
                {
                    var n = (int)Math.Min(PageSize, data.Length - p);
                    memory.Read((nint)(s + p), data.AsSpan((int)p, n));
                }
            }
            sections.Add(((nint)s, data));
        }
        return new ModuleImage(moduleBase, size, sections);
    }

    private ReadOnlySpan<byte> At(nint address, int length)
    {
        foreach (var (start, data) in _sections)
        {
            if (address >= start && address + length <= start + data.Length)
                return data.AsSpan((int)(address - start), length);
        }
        return default;
    }

    public bool TryReadUInt32(nint address, out uint value)
    {
        var s = At(address, 4);
        value = s.Length == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(s) : 0;
        return s.Length == 4;
    }

    public bool TryReadUInt64(nint address, out ulong value)
    {
        var s = At(address, 8);
        value = s.Length == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(s) : 0;
        return s.Length == 8;
    }
}

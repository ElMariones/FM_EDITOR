using System.Buffers.Binary;

namespace FMOpenEditor.Core.Memory;

/// <summary>
/// A PE file (a DLL on disk) mapped as if the loader had placed it at its preferred image base, so the
/// same scanners used on the live game (RTTI, strings, signatures) work without the game running.
/// Addresses are <c>ImageBase + RVA</c>. Pointers inside the file are not relocated, which is fine
/// while the image sits at its preferred base, as here. Read-only.
/// </summary>
public sealed class PeFileMemory : IMemoryAccess
{
    private readonly byte[] _file;
    private readonly List<Section> _sections = [];

    public sealed record Section(string Name, uint VirtualAddress, uint VirtualSize, uint RawOffset, uint RawSize, uint Characteristics)
    {
        public bool Executable => (Characteristics & 0x20000000) != 0;
        public bool Writable => (Characteristics & 0x80000000) != 0;
    }

    public PeFileMemory(string path) : this(File.ReadAllBytes(path)) { }

    public PeFileMemory(byte[] file)
    {
        _file = file;
        if (file.Length < 0x40 || file[0] != 'M' || file[1] != 'Z') throw new InvalidDataException("Not a PE file.");
        var pe = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(0x3C));
        if (BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pe)) != 0x00004550) throw new InvalidDataException("Missing PE signature.");
        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(pe + 6));
        var optSize = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(pe + 20));
        var opt = pe + 24;
        if (BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(opt)) != 0x20B) throw new InvalidDataException("Only 64-bit (PE32+) images are supported.");
        ImageBase = (nint)BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(opt + 24));
        ImageSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(opt + 56));

        var s = opt + optSize;
        for (var i = 0; i < sectionCount; i++, s += 40)
        {
            var name = System.Text.Encoding.ASCII.GetString(file, s, 8).TrimEnd('\0');
            _sections.Add(new Section(name,
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(s + 12)),
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(s + 8)),
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(s + 20)),
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(s + 16)),
                BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(s + 36))));
        }
        _sections.Sort((a, b) => a.VirtualAddress.CompareTo(b.VirtualAddress));
    }

    public nint ImageBase { get; }
    public long ImageSize { get; }
    public IReadOnlyList<Section> Sections => _sections;

    public long RvaOf(nint address) => address - ImageBase;

    public bool Read(nint address, Span<byte> buffer)
    {
        var rva = (long)(address - ImageBase);
        var pos = 0;
        while (pos < buffer.Length)
        {
            var sec = Find(rva + pos);
            if (sec is null) return false;
            var inSec = rva + pos - sec.VirtualAddress;
            var n = (int)Math.Min(buffer.Length - pos, Math.Max(sec.VirtualSize, sec.RawSize) - inSec);
            // Bytes past the raw data (e.g. .bss) are zero, as the loader would leave them.
            var rawAvail = (int)Math.Clamp(sec.RawSize - inSec, 0, n);
            if (rawAvail > 0) _file.AsSpan((int)(sec.RawOffset + inSec), rawAvail).CopyTo(buffer[pos..]);
            buffer.Slice(pos + rawAvail, n - rawAvail).Clear();
            pos += n;
        }
        return true;
    }

    public bool Write(nint address, ReadOnlySpan<byte> data) => false;

    public IEnumerable<MemoryRegion> Regions() =>
        _sections.Select(s => new MemoryRegion(ImageBase + (nint)s.VirtualAddress, Math.Max(s.VirtualSize, s.RawSize), s.Writable));

    private Section? Find(long rva)
    {
        foreach (var s in _sections)
        {
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.RawSize)) return s;
        }
        return null;
    }
}

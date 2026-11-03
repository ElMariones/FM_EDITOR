using System.Buffers.Binary;
using System.Text;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.Tests;

/// <summary>A sparse, in-process stand-in for another process's address space.</summary>
public sealed class FakeMemory : IMemoryAccess
{
    private readonly List<(nint Start, byte[] Data, bool Writable)> _regions = [];

    public FakeMemory Map(nint start, int size, bool writable = true)
    {
        _regions.Add((start, new byte[size], writable));
        _regions.Sort((a, b) => a.Start.CompareTo(b.Start));
        return this;
    }

    private (byte[] Data, int Offset)? Locate(nint address, int length)
    {
        foreach (var (start, data, _) in _regions)
        {
            if (address >= start && address + length <= start + data.Length) return (data, (int)(address - start));
        }
        return null;
    }

    public bool Read(nint address, Span<byte> buffer)
    {
        if (Locate(address, buffer.Length) is not var (data, off)) return false;
        data.AsSpan(off, buffer.Length).CopyTo(buffer);
        return true;
    }

    public bool Write(nint address, ReadOnlySpan<byte> bytes)
    {
        if (Locate(address, bytes.Length) is not var (data, off)) return false;
        bytes.CopyTo(data.AsSpan(off));
        return true;
    }

    public IEnumerable<MemoryRegion> Regions() => _regions.Select(r => new MemoryRegion(r.Start, r.Data.Length, r.Writable));

    public void Bytes(nint a, params byte[] b) => Assert.True(Write(a, b));
    public void I16(nint a, short v) { var b = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(b, v); Bytes(a, b); }
    public void I32(nint a, int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); Bytes(a, b); }
    public void I64(nint a, long v) { var b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); Bytes(a, b); }
    public void Utf16(nint a, string s) => Bytes(a, Encoding.Unicode.GetBytes(s + "\0"));
}

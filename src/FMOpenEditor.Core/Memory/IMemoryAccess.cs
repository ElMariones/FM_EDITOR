using System.Buffers.Binary;
using System.Text;

namespace FMOpenEditor.Core.Memory;

public readonly record struct MemoryRegion(nint Start, long Size, bool Writable);

/// <summary>Raw access to another process's memory (or a fake buffer in tests).</summary>
public interface IMemoryAccess
{
    bool Read(nint address, Span<byte> buffer);
    bool Write(nint address, ReadOnlySpan<byte> data);

    /// <summary>Committed, readable regions, in address order.</summary>
    IEnumerable<MemoryRegion> Regions();
}

public static class MemoryAccessExtensions
{
    public static byte[] ReadBytes(this IMemoryAccess m, nint address, int count)
    {
        var buf = new byte[count];
        if (!m.Read(address, buf)) throw new MemoryAccessException(address, count);
        return buf;
    }

    public static nint ReadPointer(this IMemoryAccess m, nint address) => (nint)m.ReadInt64(address);
    public static long ReadInt64(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadInt64LittleEndian(m.ReadBytes(a, 8));
    public static int ReadInt32(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadInt32LittleEndian(m.ReadBytes(a, 4));
    public static uint ReadUInt32(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadUInt32LittleEndian(m.ReadBytes(a, 4));
    public static short ReadInt16(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadInt16LittleEndian(m.ReadBytes(a, 2));
    public static ushort ReadUInt16(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadUInt16LittleEndian(m.ReadBytes(a, 2));
    public static byte ReadByte(this IMemoryAccess m, nint a) => m.ReadBytes(a, 1)[0];
    public static float ReadSingle(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadSingleLittleEndian(m.ReadBytes(a, 4));
    public static double ReadDouble(this IMemoryAccess m, nint a) => BinaryPrimitives.ReadDoubleLittleEndian(m.ReadBytes(a, 8));

    public static void WriteChecked(this IMemoryAccess m, nint address, ReadOnlySpan<byte> data)
    {
        if (!m.Write(address, data)) throw new MemoryAccessException(address, data.Length, write: true);
    }

    /// <summary>Reads a null-terminated string of at most <paramref name="maxChars"/> characters.</summary>
    public static string ReadString(this IMemoryAccess m, nint address, Encoding encoding, int maxChars = 256)
    {
        var charSize = encoding is UnicodeEncoding ? 2 : 1;
        var bytes = m.ReadBytes(address, maxChars * charSize);
        var end = bytes.Length;
        for (var i = 0; i + charSize <= bytes.Length; i += charSize)
        {
            if (bytes[i] == 0 && (charSize == 1 || bytes[i + 1] == 0)) { end = i; break; }
        }
        return encoding.GetString(bytes, 0, end);
    }
}

public sealed class MemoryAccessException(nint address, int count, bool write = false)
    : Exception($"Could not {(write ? "write" : "read")} {count} bytes at 0x{address:X}.")
{
    public nint Address { get; } = address;
}

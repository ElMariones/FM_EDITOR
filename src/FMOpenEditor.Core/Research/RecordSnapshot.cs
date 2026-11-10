using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.Core.Research;

/// <summary>A copy of the bytes of one record (or any block of memory) at a point in time.</summary>
public sealed class RecordSnapshot
{
    private const int PageSize = 4096;

    public RecordSnapshot(nint baseAddress, byte[] data, bool[] readable, DateTime taken)
    {
        if (data.Length != readable.Length) throw new ArgumentException("data and readable must be the same length.");
        Base = baseAddress;
        Data = data;
        Readable = readable;
        Taken = taken;
    }

    public nint Base { get; }
    public byte[] Data { get; }
    /// <summary>False for bytes on pages that couldn't be read.</summary>
    public bool[] Readable { get; }
    public DateTime Taken { get; }
    public int Length => Data.Length;

    public static RecordSnapshot Take(IMemoryAccess memory, nint baseAddress, int length)
    {
        if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
        var data = new byte[length];
        var readable = new bool[length];
        if (memory.Read(baseAddress, data))
        {
            Array.Fill(readable, true);
        }
        else
        {
            // Read up to each page boundary separately so one bad page doesn't lose the rest.
            var pos = 0;
            while (pos < length)
            {
                var addr = baseAddress + pos;
                var toBoundary = PageSize - (int)((long)addr % PageSize);
                var n = Math.Min(toBoundary, length - pos);
                if (memory.Read(addr, data.AsSpan(pos, n))) readable.AsSpan(pos, n).Fill(true);
                pos += n;
            }
        }
        return new RecordSnapshot(baseAddress, data, readable, DateTime.Now);
    }

    public bool IsReadable(int offset, int size) =>
        offset >= 0 && offset + size <= Length && !Readable.AsSpan(offset, size).Contains(false);
}

/// <summary>A run of bytes that differs between two snapshots, with readings of the numbers it might be.</summary>
public sealed record ByteChange(int Offset, byte[] Before, byte[] After, IReadOnlyList<string> Readings)
{
    public int Length => After.Length;
}

/// <summary>One line of a hex dump.</summary>
public sealed record HexRow(int Offset, string Hex, string Ascii, bool Changed);

/// <summary>An 8-byte value inside a record that points at readable memory, with a peek at what it points to.</summary>
public sealed record PointerField(int Offset, nint Target, string Description);

public static class RecordDiff
{
    /// <summary>
    /// Runs of differing bytes. Runs separated by fewer than <paramref name="mergeGap"/> equal bytes are joined,
    /// so a 4-byte number whose middle byte didn't change shows as one change.
    /// </summary>
    public static List<ByteChange> Compare(RecordSnapshot before, RecordSnapshot after, int mergeGap = 2)
    {
        var len = Math.Min(before.Length, after.Length);
        var runs = new List<(int Start, int End)>();
        for (var i = 0; i < len; i++)
        {
            if (!before.Readable[i] || !after.Readable[i] || before.Data[i] == after.Data[i]) continue;
            if (runs.Count > 0 && i - runs[^1].End <= mergeGap) runs[^1] = (runs[^1].Start, i + 1);
            else runs.Add((i, i + 1));
        }
        return runs.Select(r => new ByteChange(r.Start,
            before.Data[r.Start..r.End], after.Data[r.Start..r.End], Readings(before, after, r.Start, r.End - r.Start))).ToList();
    }

    /// <summary>The change read as each aligned number type that covers it.</summary>
    public static List<string> Readings(RecordSnapshot before, RecordSnapshot after, int offset, int length)
    {
        var list = new List<string>();
        foreach (var size in new[] { 1, 2, 4, 8 })
        {
            var at = offset / size * size;
            if (offset + length > at + size) continue; // doesn't fit in one aligned value of this size
            if (!before.IsReadable(at, size) || !after.IsReadable(at, size)) continue;
            var b = before.Data.AsSpan(at, size);
            var a = after.Data.AsSpan(at, size);
            var where = $"@0x{at:X}";
            switch (size)
            {
                case 1:
                    list.Add($"u8 {where}: {b[0]} → {a[0]}");
                    break;
                case 2:
                    list.Add($"i16 {where}: {BinaryPrimitives.ReadInt16LittleEndian(b)} → {BinaryPrimitives.ReadInt16LittleEndian(a)}");
                    break;
                case 4:
                    list.Add($"i32 {where}: {BinaryPrimitives.ReadInt32LittleEndian(b)} → {BinaryPrimitives.ReadInt32LittleEndian(a)}");
                    var fb = BinaryPrimitives.ReadSingleLittleEndian(b);
                    var fa = BinaryPrimitives.ReadSingleLittleEndian(a);
                    if (PlausibleFloat(fb) && PlausibleFloat(fa)) list.Add($"f32 {where}: {Fmt(fb)} → {Fmt(fa)}");
                    if (FmDate(b) is { } dateB && FmDate(a) is { } dateA) list.Add($"fmDate {where}: {dateB} → {dateA}");
                    break;
                case 8:
                    list.Add($"i64 {where}: {BinaryPrimitives.ReadInt64LittleEndian(b)} → {BinaryPrimitives.ReadInt64LittleEndian(a)}");
                    var db8 = BinaryPrimitives.ReadDoubleLittleEndian(b);
                    var da8 = BinaryPrimitives.ReadDoubleLittleEndian(a);
                    if (PlausibleFloat(db8) && PlausibleFloat(da8)) list.Add($"f64 {where}: {Fmt(db8)} → {Fmt(da8)}");
                    break;
            }
        }
        return list;
    }

    /// <summary>A hex dump, 16 bytes per row; rows that differ from <paramref name="previous"/> are flagged.</summary>
    public static List<HexRow> HexDump(RecordSnapshot current, RecordSnapshot? previous = null)
    {
        var rows = new List<HexRow>();
        var hex = new StringBuilder();
        var ascii = new StringBuilder();
        for (var row = 0; row < current.Length; row += 16)
        {
            hex.Clear();
            ascii.Clear();
            var changed = false;
            for (var i = row; i < row + 16; i++)
            {
                if (i == row + 8) hex.Append(' ');
                if (i >= current.Length) { hex.Append("   "); continue; }
                if (!current.Readable[i]) { hex.Append("?? "); ascii.Append('?'); continue; }
                var b = current.Data[i];
                hex.Append(b.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
                ascii.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
                if (previous is not null && i < previous.Length && previous.Readable[i] && previous.Data[i] != b) changed = true;
            }
            rows.Add(new HexRow(row, hex.ToString().TrimEnd(), ascii.ToString(), changed));
        }
        return rows;
    }

    /// <summary>
    /// Every 8-byte-aligned value in the record that points into readable memory: the record's vtable,
    /// strings (names), and sub-records (contract, finances) to follow with a "path".
    /// </summary>
    public static List<PointerField> Pointers(IMemoryAccess memory, RecordSnapshot snapshot, nint moduleBase = 0, long moduleSize = 0)
    {
        var regions = memory.Regions().OrderBy(r => r.Start).ToArray();
        var result = new List<PointerField>();
        var peek = new byte[64];
        for (var off = 0; off + 8 <= snapshot.Length; off += 8)
        {
            if (!snapshot.IsReadable(off, 8)) continue;
            var target = (nint)BinaryPrimitives.ReadInt64LittleEndian(snapshot.Data.AsSpan(off, 8));
            if (target < 0x10000 || !InRegion(regions, target)) continue;

            string description;
            if (moduleSize > 0 && target >= moduleBase && target < moduleBase + (nint)moduleSize)
                description = $"module+0x{target - moduleBase:X} (vtable or global)";
            else if (memory.Read(target, peek) || memory.Read(target, peek.AsSpan(0, 16)))
                description = PeekString(peek) ?? "heap: " + Convert.ToHexString(peek.AsSpan(0, 16)).ToLowerInvariant();
            else
                description = "heap";
            result.Add(new PointerField(off, target, description));
        }
        return result;
    }

    private static bool InRegion(MemoryRegion[] sorted, nint address)
    {
        int lo = 0, hi = sorted.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var r = sorted[mid];
            if (address < r.Start) hi = mid - 1;
            else if (address >= r.Start + (nint)r.Size) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>A printable UTF-16 or UTF-8 string of at least 3 characters at the start of <paramref name="b"/>, if any.</summary>
    public static string? PeekString(ReadOnlySpan<byte> b)
    {
        var u16 = new StringBuilder();
        for (var i = 0; i + 1 < b.Length; i += 2)
        {
            var c = (char)BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(i, 2));
            if (c == 0) break;
            if (char.IsControl(c) || char.IsSurrogate(c)) { u16.Clear(); break; }
            u16.Append(c);
        }
        if (u16.Length >= 3) return $"utf16 \"{u16}\"";

        var end = b.IndexOf((byte)0);
        if (end < 3) return null;
        var s = Encoding.UTF8.GetString(b[..end]);
        if (s.Any(c => char.IsControl(c) || c == '�')) return null;
        return $"utf8 \"{s}\"";
    }

    /// <summary>Reads 4 bytes as an FM date (u16 day of year, 0-based, then u16 year) if it looks like one.</summary>
    private static string? FmDate(ReadOnlySpan<byte> b)
    {
        var day = BinaryPrimitives.ReadUInt16LittleEndian(b);
        var year = BinaryPrimitives.ReadUInt16LittleEndian(b[2..]);
        if (year is < 1900 or > 2200 || day > 365) return null;
        return new DateTime(year, 1, 1).AddDays(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static bool PlausibleFloat(double v) =>
        double.IsFinite(v) && (v == 0 || (Math.Abs(v) >= 1e-4 && Math.Abs(v) <= 1e12));

    private static string Fmt(double v) => v.ToString("G7", CultureInfo.InvariantCulture);
}

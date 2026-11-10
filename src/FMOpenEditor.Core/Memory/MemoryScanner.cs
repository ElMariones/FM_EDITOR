using System.Buffers.Binary;

namespace FMOpenEditor.Core.Memory;

public enum ScanValueType { Byte, Int16, Int32, Int64, Float, Double }

public enum ScanCompare { Exact, Changed, Unchanged, Increased, Decreased }

/// <summary>
/// A small value scanner for contributors mapping a new game version: search for a value you can
/// see in the game (a wage, a balance), change it in-game, scan again, and the few addresses left
/// point at the field. Their distance from a known record base is the offset for the definition file.
/// </summary>
public sealed class MemoryScanner(IMemoryAccess memory)
{
    public const int MaxResults = 2_000_000;

    private List<nint> _addresses = [];
    private Dictionary<nint, double> _lastValues = [];

    public ScanValueType ValueType { get; private set; } = ScanValueType.Int32;
    public IReadOnlyList<nint> Results => _addresses;

    public static int SizeOf(ScanValueType t) => t switch
    {
        ScanValueType.Byte => 1,
        ScanValueType.Int16 => 2,
        ScanValueType.Int32 or ScanValueType.Float => 4,
        _ => 8,
    };

    /// <summary>Scans every writable region for <paramref name="value"/>. Aligned to the value size.</summary>
    public int FirstScan(double value, ScanValueType type, CancellationToken ct = default)
    {
        ValueType = type;
        var size = SizeOf(type);
        var results = new List<nint>();
        var values = new Dictionary<nint, double>();
        var buffer = new byte[1024 * 1024];

        foreach (var region in memory.Regions().Where(r => r.Writable))
        {
            for (long off = 0; off < region.Size && results.Count < MaxResults; off += buffer.Length)
            {
                ct.ThrowIfCancellationRequested();
                var len = (int)Math.Min(buffer.Length, region.Size - off);
                var span = buffer.AsSpan(0, len);
                if (!memory.Read(region.Start + (nint)off, span)) continue;

                for (var i = 0; i + size <= len; i += size)
                {
                    if (Decode(span.Slice(i, size), type) == value)
                    {
                        var addr = region.Start + (nint)off + i;
                        results.Add(addr);
                        values[addr] = value;
                    }
                }
            }
        }

        _addresses = results;
        _lastValues = values;
        return results.Count;
    }

    /// <summary>Keeps only the previous results that satisfy <paramref name="compare"/>.</summary>
    public int NextScan(ScanCompare compare, double value = 0)
    {
        var size = SizeOf(ValueType);
        var buf = new byte[size];
        var kept = new List<nint>();
        var values = new Dictionary<nint, double>();

        foreach (var addr in _addresses)
        {
            if (!memory.Read(addr, buf)) continue;
            var now = Decode(buf, ValueType);
            var before = _lastValues[addr];
            var keep = compare switch
            {
                ScanCompare.Exact => now == value,
                ScanCompare.Changed => now != before,
                ScanCompare.Unchanged => now == before,
                ScanCompare.Increased => now > before,
                ScanCompare.Decreased => now < before,
                _ => false,
            };
            if (keep) { kept.Add(addr); values[addr] = now; }
        }

        _addresses = kept;
        _lastValues = values;
        return kept.Count;
    }

    public double? ReadValue(nint address)
    {
        var buf = new byte[SizeOf(ValueType)];
        return memory.Read(address, buf) ? Decode(buf, ValueType) : null;
    }

    public bool WriteValue(nint address, double value)
    {
        var buf = new byte[SizeOf(ValueType)];
        Encode(buf, ValueType, value);
        return memory.Write(address, buf);
    }

    public void Reset()
    {
        _addresses = [];
        _lastValues = [];
    }

    public static double Decode(ReadOnlySpan<byte> b, ScanValueType t) => t switch
    {
        ScanValueType.Byte => b[0],
        ScanValueType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(b),
        ScanValueType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(b),
        ScanValueType.Int64 => BinaryPrimitives.ReadInt64LittleEndian(b),
        ScanValueType.Double => BinaryPrimitives.ReadDoubleLittleEndian(b),
        _ => BinaryPrimitives.ReadSingleLittleEndian(b),
    };

    public static void Encode(Span<byte> b, ScanValueType t, double v)
    {
        switch (t)
        {
            case ScanValueType.Byte: b[0] = (byte)v; break;
            case ScanValueType.Int16: BinaryPrimitives.WriteInt16LittleEndian(b, (short)v); break;
            case ScanValueType.Int32: BinaryPrimitives.WriteInt32LittleEndian(b, (int)v); break;
            case ScanValueType.Int64: BinaryPrimitives.WriteInt64LittleEndian(b, (long)v); break;
            case ScanValueType.Double: BinaryPrimitives.WriteDoubleLittleEndian(b, v); break;
            default: BinaryPrimitives.WriteSingleLittleEndian(b, (float)v); break;
        }
    }
}

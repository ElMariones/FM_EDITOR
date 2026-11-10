using System.Buffers.Binary;
using System.Globalization;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.Core.Research;

/// <summary>A record and the value the game shows for the field you're looking for.</summary>
public readonly record struct CorrelationSample(nint RecordBase, double Shown);

/// <summary>How the shown value is derived from the stored one.</summary>
public enum CorrelationKind
{
    /// <summary>stored = shown × scale, exactly.</summary>
    Exact,
    /// <summary>shown = round(stored ÷ scale).</summary>
    Rounded,
    /// <summary>shown = floor(stored ÷ scale).</summary>
    Floored,
}

/// <summary>
/// An offset whose stored value explains the shown value for every sample. <see cref="Type"/> and
/// <see cref="Scale"/> use the definition file's names, so a candidate can be copied straight into it.
/// </summary>
public sealed record CorrelationCandidate(int Offset, string Type, double Scale, CorrelationKind Kind, IReadOnlyList<double> Stored)
{
    public string Formula => Kind switch
    {
        CorrelationKind.Exact => Scale == 1 ? "stored = shown" : $"stored = shown × {Fmt(Scale)}",
        CorrelationKind.Rounded => $"shown = round(stored ÷ {Fmt(Scale)})",
        _ => $"shown = floor(stored ÷ {Fmt(Scale)})",
    };

    private static string Fmt(double v) => v.ToString("G6", CultureInfo.InvariantCulture);
}

public sealed record CorrelationResult(IReadOnlyList<CorrelationCandidate> Candidates, IReadOnlyList<string> Warnings);

/// <summary>
/// Finds a field's offset by comparing many records at once. Give it several records (players, say)
/// and the value the game shows for each, e.g. their Finishing. It tries every offset and number type
/// and keeps the ones where the stored value explains the shown value for all of them. With 10+
/// samples whose values differ, usually only the real field survives, and you also learn its scale.
/// </summary>
public static class FieldCorrelator
{
    private static readonly (string Name, int Size)[] Types =
        [("u8", 1), ("i8", 1), ("u16", 2), ("i16", 2), ("u32", 4), ("i32", 4), ("i64", 8), ("f32", 4), ("f64", 8)];

    /// <summary>Divisors tried for rounded/floored matches (FM has stored 1–20 attributes as 1–100).</summary>
    public static readonly double[] CommonScales = [2, 4, 5, 10, 20, 100, 1000];

    public static CorrelationResult Correlate(IMemoryAccess memory, IReadOnlyList<CorrelationSample> samples,
        int recordLength, int maxCandidates = 200, CancellationToken ct = default)
    {
        var snapshots = samples.Select(s => RecordSnapshot.Take(memory, s.RecordBase, recordLength)).ToList();
        return Correlate(snapshots, samples.Select(s => s.Shown).ToList(), maxCandidates, ct);
    }

    public static CorrelationResult Correlate(IReadOnlyList<RecordSnapshot> records, IReadOnlyList<double> shown,
        int maxCandidates = 200, CancellationToken ct = default)
    {
        if (records.Count != shown.Count) throw new ArgumentException("One shown value is needed per record.");
        var warnings = new List<string>();
        if (records.Count < 2) return new CorrelationResult([], ["Add at least two records (ten or more is best)."]);

        var distinct = shown.Distinct().Count();
        if (distinct < 3)
            warnings.Add($"Only {distinct} distinct shown value(s): expect false matches. Pick records whose values differ.");
        else if (records.Count < 6)
            warnings.Add("Fewer than six records: some matches may be coincidences. Add more to narrow it down.");

        var length = records.Min(r => r.Length);
        var candidates = new List<CorrelationCandidate>();
        var stored = new double[records.Count];

        for (var offset = 0; offset < length; offset++)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var (type, size) in Types)
            {
                if (offset % size != 0 || offset + size > length) continue; // the game aligns fields to their size
                var ok = true;
                for (var i = 0; i < records.Count && ok; i++)
                {
                    if (!records[i].IsReadable(offset, size)) { ok = false; break; }
                    stored[i] = Read(records[i].Data.AsSpan(offset, size), type);
                    if (!double.IsFinite(stored[i])) ok = false;
                }
                if (!ok) continue;
                if (Match(stored, shown, type) is { } m)
                    candidates.Add(new CorrelationCandidate(offset, type, m.Scale, m.Kind, stored.ToArray()));
            }
        }

        // A field that is genuinely u8 also "matches" as u16/u32 when the following bytes happen to be
        // zero in every sample. Keep the smallest type per offset.
        var best = candidates
            .GroupBy(c => c.Offset)
            .Select(g => g.OrderBy(c => c.Kind).ThenBy(c => SizeOf(c.Type)).ThenBy(c => c.Type.StartsWith('u') ? 0 : 1).First())
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.Scale == 1 ? 0 : 1)
            .ThenBy(c => c.Offset)
            .Take(maxCandidates)
            .ToList();

        if (best.Count == 0)
            warnings.Add("No offset matched. Check the shown values, try a longer record length, or the field may sit behind a pointer.");
        return new CorrelationResult(best, warnings);
    }

    private static (double Scale, CorrelationKind Kind)? Match(double[] stored, IReadOnlyList<double> shown, string type)
    {
        // All-zero stored values "match" any all-zero shown values but say nothing.
        if (stored.All(v => v == 0)) return null;

        var isFloat = type is "f32" or "f64";
        var tolerance = type == "f32" ? 1e-4 : 1e-9;

        // Integer bytes read as floats come out as denormals or huge numbers; they'd "match" at a ~0 scale.
        if (isFloat && stored.Any(v => v != 0 && (Math.Abs(v) < 1e-6 || Math.Abs(v) > 1e15))) return null;

        // Exact ratio, taken from the first sample with a nonzero shown value.
        var pivot = -1;
        for (var i = 0; i < shown.Count; i++) if (shown[i] != 0) { pivot = i; break; }
        if (pivot >= 0)
        {
            var k = stored[pivot] / shown[pivot];
            if (k is >= 1e-6 and <= 1e9)
            {
                var all = true;
                for (var i = 0; i < stored.Length && all; i++)
                {
                    var expected = shown[i] * k;
                    all = Math.Abs(stored[i] - expected) <= tolerance * Math.Max(1, Math.Abs(expected));
                }
                if (all) return (Clean(k), CorrelationKind.Exact);
            }
        }

        if (isFloat) return null;
        foreach (var scale in CommonScales)
        {
            if (stored.Select((v, i) => Math.Round(v / scale, MidpointRounding.AwayFromZero) == shown[i]).All(x => x))
                return (scale, CorrelationKind.Rounded);
            if (stored.Select((v, i) => Math.Floor(v / scale) == shown[i]).All(x => x))
                return (scale, CorrelationKind.Floored);
        }
        return null;
    }

    private static double Clean(double k)
    {
        var r = Math.Round(k);
        return Math.Abs(k - r) < 1e-6 ? r : k;
    }

    private static int SizeOf(string type) => Types.First(t => t.Name == type).Size;

    private static double Read(ReadOnlySpan<byte> b, string type) => type switch
    {
        "u8" => b[0],
        "i8" => (sbyte)b[0],
        "u16" => BinaryPrimitives.ReadUInt16LittleEndian(b),
        "i16" => BinaryPrimitives.ReadInt16LittleEndian(b),
        "u32" => BinaryPrimitives.ReadUInt32LittleEndian(b),
        "i32" => BinaryPrimitives.ReadInt32LittleEndian(b),
        "i64" => BinaryPrimitives.ReadInt64LittleEndian(b),
        "f32" => BinaryPrimitives.ReadSingleLittleEndian(b),
        _ => BinaryPrimitives.ReadDoubleLittleEndian(b),
    };
}

using System.Globalization;

namespace FMOpenEditor.Core.Memory;

/// <summary>
/// Finds byte patterns ("signatures") such as <c>48 8B 05 ?? ?? ?? ?? 48 85 C0</c> in a module.
/// Signatures survive most game patches, unlike raw offsets, so definitions locate the game's
/// global tables through them.
/// </summary>
public static class SignatureScanner
{
    private const int ChunkSize = 4 * 1024 * 1024;

    /// <summary>Parses a pattern; <c>?</c> or <c>??</c> is a wildcard byte.</summary>
    public static byte?[] Parse(string pattern) =>
        pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries)
               .Select(t => t.Trim('?').Length == 0 ? (byte?)null : byte.Parse(t, NumberStyles.HexNumber))
               .ToArray();

    /// <summary>Returns the address of the first match inside [start, start+size), or null.</summary>
    public static nint? Find(IMemoryAccess memory, nint start, long size, string pattern)
    {
        var sig = Parse(pattern);
        if (sig.Length == 0) throw new ArgumentException("Empty signature.", nameof(pattern));

        var buffer = new byte[ChunkSize + sig.Length];
        for (long offset = 0; offset < size; offset += ChunkSize)
        {
            var len = (int)Math.Min(buffer.Length, size - offset);
            if (len < sig.Length) break;
            if (!memory.Read(start + (nint)offset, buffer.AsSpan(0, len))) continue;

            var hit = IndexOf(buffer.AsSpan(0, len), sig);
            if (hit >= 0) return start + (nint)offset + hit;
        }
        return null;
    }

    public static int IndexOf(ReadOnlySpan<byte> haystack, byte?[] sig)
    {
        // Anchor on the first concrete byte so we can use the vectorised IndexOf to skip ahead.
        var anchor = Array.FindIndex(sig, b => b.HasValue);
        if (anchor < 0) return 0;
        var anchorByte = sig[anchor]!.Value;

        var pos = anchor;
        while (pos <= haystack.Length - sig.Length + anchor)
        {
            var next = haystack[pos..].IndexOf(anchorByte);
            if (next < 0) return -1;
            pos += next;
            var startAt = pos - anchor;
            if (startAt + sig.Length > haystack.Length) return -1;
            if (Matches(haystack.Slice(startAt, sig.Length), sig)) return startAt;
            pos++;
        }
        return -1;
    }

    private static bool Matches(ReadOnlySpan<byte> data, byte?[] sig)
    {
        for (var i = 0; i < sig.Length; i++)
        {
            if (sig[i] is { } b && data[i] != b) return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves an x64 RIP-relative operand: the 32-bit displacement at
    /// <paramref name="match"/> + <paramref name="dispOffset"/> is relative to the end of the instruction.
    /// </summary>
    public static nint ResolveRipRelative(IMemoryAccess memory, nint match, int dispOffset, int instructionLength) =>
        match + instructionLength + memory.ReadInt32(match + dispOffset);
}

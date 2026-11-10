using System.Globalization;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.App.ViewModels.Research;

/// <summary>What every Research tool shares: the attached game, and a way to hand addresses to the other tools.</summary>
public sealed class ResearchContext(IMemoryAccess memory, nint moduleBase, long moduleSize, string moduleName)
{
    public IMemoryAccess Memory { get; } = memory;
    public nint ModuleBase { get; } = moduleBase;
    public long ModuleSize { get; } = moduleSize;
    public string ModuleName { get; } = moduleName;
    public bool HasModule => ModuleSize > 0;

    /// <summary>Set by the Research page: makes an address the record base in the record and offset tools.</summary>
    public Action<nint> UseAsRecordBase { get; set; } = _ => { };
    /// <summary>Set by the Research page: adds an address as a record in the correlator.</summary>
    public Action<nint> AddToCorrelator { get; set; } = _ => { };
    /// <summary>Set by the Research page: searches for pointers to an address.</summary>
    public Action<nint> FindPointersTo { get; set; } = _ => { };

    public bool InModule(nint address) => HasModule && address >= ModuleBase && address < ModuleBase + (nint)ModuleSize;

    /// <summary>"0x1A2B3C" or, inside the game module, "game_plugin.dll+0x1234".</summary>
    public string Describe(nint address) =>
        InModule(address) ? $"{ModuleName}+0x{address - ModuleBase:X}" : ResearchFormat.Hex(address);
}

public static class ResearchFormat
{
    public static string Hex(nint value) => $"0x{(long)value:X}";
    public static string Hex(long value) => value < 0 ? $"-0x{-value:X}" : $"0x{value:X}";

    public static bool TryNumber(string? s, out double v) =>
        double.TryParse(s?.Replace(",", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>Parses hex, with or without 0x. Accepts "module+0x123" style input if a context is given.</summary>
    public static bool TryHex(string? s, out long v, ResearchContext? context = null)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().Replace("`", "");
        var plus = t.LastIndexOf('+');
        if (plus > 0 && context is { HasModule: true } &&
            t[..plus].Trim().Equals(context.ModuleName, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryHex(t[(plus + 1)..], out var off)) return false;
            v = context.ModuleBase + off;
            return true;
        }
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>A length typed as hex (0x800) or decimal (2048).</summary>
    public static bool TryLength(string? s, out int length)
    {
        length = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        long v;
        var ok = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)
            : long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        if (!ok || v <= 0 || v > 1 << 20) return false;
        length = (int)v;
        return true;
    }
}

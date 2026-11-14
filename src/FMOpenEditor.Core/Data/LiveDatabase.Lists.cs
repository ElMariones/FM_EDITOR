using System.Buffers.Binary;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Core.Data;

public sealed partial class LiveDatabase
{
    private const int MaxEntries = 256;

    /// <summary>Address of the first entry of <paramref name="list"/> whose key matches, or null.</summary>
    private (nint Begin, nint End, nint Entry)? FindEntry(nint list, ListEntryDef entry)
    {
        var begin = _memory.ReadPointer(list);
        var end = _memory.ReadPointer(list + 8);
        if (begin == 0 || end <= begin || (end - begin) % entry.Stride != 0 || (end - begin) / entry.Stride > MaxEntries)
            return null;
        var keyAt = (nint)GameDefinition.ParseOffset(entry.KeyOffset);
        for (var e = begin; e < end; e += entry.Stride)
            if ((long)ReadNumber(e + keyAt, entry.KeyType) == entry.Key) return (begin, end, e);
        return null;
    }

    private object? ReadEntry(nint list, FieldDef field, ListEntryDef entry, ValueKind kind)
    {
        if (FindEntry(list, entry) is not { } found) return null;
        var raw = (long)ReadNumber(found.Entry + (nint)GameDefinition.ParseOffset(entry.ValueOffset), field.Type);
        if (entry.NoValue is { } none && raw == none) return null;
        var shown = Math.Round(raw / field.Scale);
        return kind == ValueKind.Money ? (object)(long)shown : (int)shown;
    }

    private void WriteEntry(nint list, FieldDef field, ListEntryDef entry, PropertyDef prop, object? value)
    {
        var found = FindEntry(list, entry);
        var amount = value is null ? 0 : Values.ToDouble(value);
        if (amount == 0)
        {
            if (found is not { } f) return;
            // Take the entry out: the ones after it move up, and the list ends one entry earlier.
            var tail = (int)(f.End - f.Entry - entry.Stride);
            if (tail > 0) Put(f.Entry, _memory.ReadBytes(f.Entry + entry.Stride, tail));
            Put(f.End - entry.Stride, new byte[entry.Stride]);
            Put(list + 8, BitConverter.GetBytes((long)(f.End - entry.Stride)));
            return;
        }
        if (found is not { } at)
            throw new NotSupportedException(
                $"This contract has no {prop.Label.ToLowerInvariant()} to change, and adding one needs room the game's list may not have.");
        WriteNumber(at.Entry + (nint)GameDefinition.ParseOffset(entry.ValueOffset), field, amount * field.Scale);
    }
}

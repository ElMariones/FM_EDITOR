using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Research;

namespace FMOpenEditor.App.ViewModels.Research;

public sealed record HexRowView(string OffsetText, string Hex, string Ascii, bool Changed);
public sealed record ChangeRow(int Offset, string OffsetText, string Before, string After, string Readings);
public sealed record PointerRow(nint Target, string OffsetText, string TargetText, string Description);

/// <summary>
/// Snapshot a record, change one thing in the game, snapshot again: the bytes that changed are the field.
/// Also lists the record's pointers (vtable, names, sub-records).
/// </summary>
public sealed partial class RecordDiffViewModel(ResearchContext context) : ObservableObject
{
    private RecordSnapshot? _previous;
    private RecordSnapshot? _current;

    public ObservableCollection<HexRowView> Rows { get; } = [];
    public ObservableCollection<ChangeRow> Changes { get; } = [];
    public ObservableCollection<PointerRow> Pointers { get; } = [];

    [ObservableProperty] private string? _baseAddress;
    [ObservableProperty] private string _length = "0x400";
    [ObservableProperty] private string _status = "Enter a record base, take a snapshot, change one thing in the game, take another.";
    [ObservableProperty] private bool _onlyChangedRows;
    [ObservableProperty] private PointerRow? _selectedPointer;

    [RelayCommand]
    private void Snapshot()
    {
        if (!ResearchFormat.TryHex(BaseAddress, out var b, context)) { Status = "Enter the record base in hex."; return; }
        if (!ResearchFormat.TryLength(Length, out var len)) { Status = "Enter a length, e.g. 0x400 (max 1 MB)."; return; }

        var snap = RecordSnapshot.Take(context.Memory, (nint)b, len);
        // A different address or length starts a new comparison.
        _previous = _current is { } c && c.Base == snap.Base && c.Length == snap.Length ? c : null;
        _current = snap;
        Refresh();

        if (!snap.Readable.Contains(true)) Status = "Nothing readable at that address.";
        else if (_previous is null) Status = $"Snapshot 1 taken at {snap.Taken:T}. Change one thing in the game, then take another.";
        else Status = $"Compared {_previous.Taken:T} → {snap.Taken:T}: {Changes.Count} changed run(s). " +
                      "Values that change on their own (timers, counters) show up too: snapshot again without changing anything to rule them out.";
    }

    [RelayCommand]
    private void Clear()
    {
        _previous = _current = null;
        Rows.Clear();
        Changes.Clear();
        Pointers.Clear();
        Status = "Cleared.";
    }

    partial void OnOnlyChangedRowsChanged(bool value) => RefreshRows();

    private void Refresh()
    {
        RefreshRows();
        Changes.Clear();
        if (_previous is not null && _current is not null)
        {
            foreach (var ch in RecordDiff.Compare(_previous, _current))
            {
                Changes.Add(new ChangeRow(ch.Offset, $"0x{ch.Offset:X}", Convert.ToHexString(ch.Before), Convert.ToHexString(ch.After),
                    string.Join("   ", ch.Readings)));
            }
        }
        Pointers.Clear();
        if (_current is not null)
        {
            foreach (var p in RecordDiff.Pointers(context.Memory, _current, context.ModuleBase, context.ModuleSize))
                Pointers.Add(new PointerRow(p.Target, $"0x{p.Offset:X}", ResearchFormat.Hex(p.Target), p.Description));
        }
    }

    private void RefreshRows()
    {
        Rows.Clear();
        if (_current is null) return;
        foreach (var r in RecordDiff.HexDump(_current, _previous))
        {
            if (OnlyChangedRows && !r.Changed) continue;
            Rows.Add(new HexRowView($"{r.Offset:X4}", r.Hex, r.Ascii, r.Changed));
        }
    }

    /// <summary>Opens the record a pointer field points to (a sub-record: contract, finances…).</summary>
    [RelayCommand]
    private void FollowPointer()
    {
        if (SelectedPointer is not { } p) return;
        BaseAddress = ResearchFormat.Hex(p.Target);
        _current = null;
        Snapshot();
        Status = $"Following pointer at offset {p.OffsetText}: in the definition, fields here use \"path\": [\"{p.OffsetText}\"]. " + Status;
    }

    public void SetBase(nint address)
    {
        BaseAddress = ResearchFormat.Hex(address);
    }
}

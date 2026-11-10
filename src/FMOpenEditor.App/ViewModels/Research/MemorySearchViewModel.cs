using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Research;

namespace FMOpenEditor.App.ViewModels.Research;

public sealed record AddressRow(nint Address, string AddressText, string Detail);

/// <summary>String search (find a record by a name) and pointer search ("what points here?").</summary>
public sealed partial class MemorySearchViewModel(ResearchContext context) : ObservableObject
{
    private const int MaxShown = 2000;

    public IReadOnlyList<string> Encodings { get; } = ["UTF-16", "UTF-8"];
    public ObservableCollection<AddressRow> Results { get; } = [];

    [ObservableProperty] private string? _text;
    [ObservableProperty] private string _encoding = "UTF-16";
    [ObservableProperty] private string? _pointerTarget;
    [ObservableProperty] private string _maxOffset = "0x0";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private AddressRow? _selected;

    [RelayCommand]
    private async Task FindString()
    {
        if (string.IsNullOrEmpty(Text)) { Status = "Type a name to search for, exactly as the game shows it."; return; }
        var enc = Encoding == "UTF-8" ? System.Text.Encoding.UTF8 : System.Text.Encoding.Unicode;
        var text = Text;
        await Run($"Searching for \"{text}\"…", () =>
        {
            var hits = ReferenceScanner.FindString(context.Memory, text, enc);
            var peek = new byte[96];
            return (hits.Select(a =>
            {
                var detail = context.Memory.Read(a, peek) ? RecordDiff.PeekString(peek) ?? "" : "";
                return new AddressRow(a, context.Describe(a), detail);
            }).ToList(), $"{hits.Count:N0} match(es). Next: select one and find pointers to it.");
        });
    }

    [RelayCommand]
    private async Task FindPointers()
    {
        if (!ResearchFormat.TryHex(PointerTarget, out var target, context)) { Status = "Enter the target address in hex."; return; }
        if (!ResearchFormat.TryHex(MaxOffset, out var max) || max < 0) max = 0;
        await Run($"Searching for pointers to {ResearchFormat.Hex(target)}…", () =>
        {
            var hits = ReferenceScanner.FindPointers(context.Memory, (nint)target, max);
            var rows = hits.Select(h => new AddressRow(h.Address, context.Describe(h.Address),
                (h.Offset == 0 ? "→ target" : $"→ target - 0x{h.Offset:X}") +
                (context.InModule(h.Address) ? "  ★ inside the module: candidate global/root" : ""))).ToList();
            var inModule = hits.Count(h => context.InModule(h.Address));
            return (rows, $"{hits.Count:N0} pointer(s), {inModule} inside {context.ModuleName}.");
        });
    }

    /// <summary>Called from other tools.</summary>
    public void SearchPointersTo(nint address)
    {
        PointerTarget = ResearchFormat.Hex(address);
        FindPointersCommand.Execute(null);
    }

    [RelayCommand]
    private void PointersToSelected()
    {
        if (Selected is { } s) SearchPointersTo(s.Address);
    }

    [RelayCommand]
    private void UseSelectedAsBase()
    {
        if (Selected is { } s) context.UseAsRecordBase(s.Address);
    }

    private async Task Run(string busyText, Func<(List<AddressRow> Rows, string Summary)> work)
    {
        IsBusy = true;
        Status = busyText;
        try
        {
            var (rows, summary) = await Task.Run(work);
            Results.Clear();
            foreach (var r in rows.Take(MaxShown)) Results.Add(r);
            Status = rows.Count > MaxShown ? $"{summary} Showing the first {MaxShown:N0}." : summary;
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }
}

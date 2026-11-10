using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.App.ViewModels.Research;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.App.ViewModels;

public sealed record ScanHit(nint Address, string AddressText, string Value);

/// <summary>
/// Tools for contributors mapping a game version: value, string and pointer scans, an RTTI class
/// finder, a record snapshot/diff, a multi-record correlator and an offset calculator. The workflow
/// is in docs/DEFINITIONS.md.
/// </summary>
public sealed partial class ResearchViewModel : ObservableObject
{
    private readonly MemoryScanner? _scanner;

    public ResearchViewModel(IMemoryAccess? memory, string? moduleInfo, nint moduleBase = 0, long moduleSize = 0)
    {
        _scanner = memory is null ? null : new MemoryScanner(memory);
        ModuleInfo = moduleInfo ?? "Not attached to the game.";
        if (memory is null) return;

        var context = new ResearchContext(memory, moduleBase, moduleSize, GameProcess.DefaultModule);
        Search = new MemorySearchViewModel(context);
        Rtti = new RttiViewModel(context);
        Diff = new RecordDiffViewModel(context);
        Correlator = new CorrelatorViewModel(context);
        context.UseAsRecordBase = a =>
        {
            RecordBase = ResearchFormat.Hex(a);
            Diff.SetBase(a);
            SelectedTab = RecordTab;
        };
        context.AddToCorrelator = a => Correlator.AddSample(a);
        context.FindPointersTo = a =>
        {
            SelectedTab = SearchTab;
            Search.SearchPointersTo(a);
        };
    }

    public const int SearchTab = 1;
    public const int RecordTab = 3;

    public MemorySearchViewModel? Search { get; }
    public RttiViewModel? Rtti { get; }
    public RecordDiffViewModel? Diff { get; }
    public CorrelatorViewModel? Correlator { get; }

    [ObservableProperty] private int _selectedTab;

    public bool IsAvailable => _scanner is not null;
    public string ModuleInfo { get; }
    public IReadOnlyList<ScanValueType> Types { get; } = Enum.GetValues<ScanValueType>();
    public IReadOnlyList<ScanCompare> Compares { get; } = Enum.GetValues<ScanCompare>();
    public ObservableCollection<ScanHit> Hits { get; } = [];

    [ObservableProperty] private string? _value;
    [ObservableProperty] private ScanValueType _type = ScanValueType.Int32;
    [ObservableProperty] private ScanCompare _compare = ScanCompare.Exact;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private ScanHit? _selectedHit;
    [ObservableProperty] private string? _writeValue;

    [ObservableProperty] private string? _recordBase;
    [ObservableProperty] private string? _fieldAddress;
    [ObservableProperty] private string _offsetResult = "";

    [RelayCommand]
    private async Task FirstScan()
    {
        if (_scanner is null || !TryNumber(Value, out var v)) { ResultText = "Enter a number to search for."; return; }
        IsBusy = true;
        ResultText = "Scanning…";
        try
        {
            var n = await Task.Run(() => _scanner.FirstScan(v, Type));
            ShowHits($"{n:N0} addresses hold {v}.");
        }
        catch (Exception ex) { ResultText = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task NextScan()
    {
        if (_scanner is null) return;
        double v = 0;
        if (Compare == ScanCompare.Exact && !TryNumber(Value, out v)) { ResultText = "Enter the new value."; return; }
        IsBusy = true;
        try
        {
            var n = await Task.Run(() => _scanner.NextScan(Compare, v));
            ShowHits($"{n:N0} addresses left.");
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Reset()
    {
        _scanner?.Reset();
        Hits.Clear();
        ResultText = "";
    }

    [RelayCommand]
    private void Write()
    {
        if (_scanner is null || SelectedHit is null || !TryNumber(WriteValue, out var v)) return;
        ResultText = _scanner.WriteValue(SelectedHit.Address, v) ? $"Wrote {v} at {SelectedHit.AddressText}." : "Write failed.";
        ShowHits(ResultText);
    }

    partial void OnSelectedHitChanged(ScanHit? value)
    {
        if (value is not null) FieldAddress = value.AddressText;
    }

    [RelayCommand]
    private void PointersToHit()
    {
        if (SelectedHit is null || Search is null) return;
        SelectedTab = SearchTab;
        Search.SearchPointersTo(SelectedHit.Address);
    }

    partial void OnRecordBaseChanged(string? value) => CalcOffset();
    partial void OnFieldAddressChanged(string? value) => CalcOffset();

    private void CalcOffset()
    {
        if (TryHex(RecordBase, out var b) && TryHex(FieldAddress, out var f))
            OffsetResult = f >= b ? $"offset 0x{f - b:X}" : "field address is below the record base";
        else OffsetResult = "";
    }

    private void ShowHits(string summary)
    {
        Hits.Clear();
        if (_scanner is null) return;
        foreach (var a in _scanner.Results.Take(500))
        {
            var cur = _scanner.ReadValue(a);
            Hits.Add(new ScanHit(a, $"0x{a:X}", cur?.ToString(CultureInfo.InvariantCulture) ?? "?"));
        }
        ResultText = _scanner.Results.Count > 500 ? $"{summary} Showing the first 500." : summary;
    }

    private static bool TryNumber(string? s, out double v) =>
        double.TryParse(s?.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryHex(string? s, out long v)
    {
        v = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        return long.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FMOpenEditor.Core.Research;

namespace FMOpenEditor.App.ViewModels.Research;

public sealed partial class RttiRow(RttiClass cls, ResearchContext context) : ObservableObject
{
    public RttiClass Class { get; } = cls;
    public string Name => Class.Name;
    public string VtableText => Class.PrimaryVtable is { } v
        ? context.Describe(v.Address) + (Class.Vtables.Count > 1 ? $" (+{Class.Vtables.Count - 1})" : "")
        : "no vtable found";

    [ObservableProperty] private string _instancesText = "";
}

/// <summary>Lists the module's C++ classes through RTTI and finds their objects in the heap.</summary>
public sealed partial class RttiViewModel(ResearchContext context) : ObservableObject
{
    private const int MaxListed = 5000;
    private List<RttiRow> _all = [];

    public bool HasModule => context.HasModule;
    public ObservableCollection<RttiRow> Classes { get; } = [];
    public ObservableCollection<AddressRow> Instances { get; } = [];

    [ObservableProperty] private string? _filter;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private RttiRow? _selectedClass;
    [ObservableProperty] private AddressRow? _selectedInstance;

    [RelayCommand]
    private async Task FindClasses()
    {
        IsBusy = true;
        Status = $"Reading {context.ModuleName} and looking for RTTI…";
        try
        {
            var classes = await Task.Run(() => RttiScanner.FindClasses(context.Memory, context.ModuleBase, context.ModuleSize));
            _all = classes.Select(c => new RttiRow(c, context)).ToList();
            ApplyFilter();
            var withVtable = classes.Count(c => c.Vtables.Count > 0);
            Status = classes.Count == 0
                ? "No RTTI found. The module may be built without it; use strings and pointers instead."
                : $"{classes.Count:N0} classes, {withVtable:N0} with a vtable. Filter by name (try person, player, club, staff), then count objects.";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    partial void OnFilterChanged(string? value) => ApplyFilter();

    private void ApplyFilter()
    {
        Classes.Clear();
        var f = Filter?.Trim();
        var rows = string.IsNullOrEmpty(f) ? _all : _all.Where(r => r.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
        foreach (var r in rows.Take(MaxListed)) Classes.Add(r);
    }

    /// <summary>Counts objects for every class in the (filtered) list in a single pass over the heap.</summary>
    [RelayCommand]
    private async Task CountListed()
    {
        var rows = Classes.Where(r => r.Class.PrimaryVtable is not null).ToList();
        if (rows.Count == 0) { Status = "Nothing to count: find classes first, or no listed class has a vtable."; return; }
        await Count(rows, showInstancesOf: rows.Count == 1 ? rows[0] : null);
    }

    [RelayCommand]
    private async Task CountSelected()
    {
        if (SelectedClass is not { Class.PrimaryVtable: not null } row) { Status = "Select a class with a vtable."; return; }
        await Count([row], row);
    }

    private async Task Count(List<RttiRow> rows, RttiRow? showInstancesOf)
    {
        IsBusy = true;
        Status = $"Scanning the heap for objects of {rows.Count:N0} class(es)…";
        try
        {
            var vtables = rows.Select(r => r.Class.PrimaryVtable!).DistinctBy(v => v.Address).ToList();
            var result = await Task.Run(() => RttiScanner.FindInstances(context.Memory, vtables, context.ModuleBase, context.ModuleSize));
            foreach (var r in rows)
            {
                var n = result.Counts.GetValueOrDefault(r.Class.PrimaryVtable!.Address);
                r.InstancesText = $"{n:N0} objects";
            }
            if (showInstancesOf is not null)
            {
                Instances.Clear();
                foreach (var a in result.Samples.GetValueOrDefault(showInstancesOf.Class.PrimaryVtable!.Address) ?? [])
                    Instances.Add(new AddressRow(a, ResearchFormat.Hex(a), ""));
            }
            var top = rows.OrderByDescending(r => result.Counts.GetValueOrDefault(r.Class.PrimaryVtable!.Address)).First();
            Status = rows.Count == 1
                ? $"{top.Name}: {top.InstancesText}. Each address is an object's base. Use one as the record base, or add several to the correlator."
                : $"Counted {rows.Count:N0} classes. Most objects: {top.Name} ({top.InstancesText}). A class with roughly as many objects as the game has people is a good lead.";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void UseInstanceAsBase()
    {
        if (SelectedInstance is { } i) context.UseAsRecordBase(i.Address);
    }

    [RelayCommand]
    private void AddInstanceToCorrelator()
    {
        if (SelectedInstance is { } i) context.AddToCorrelator(i.Address);
    }

    [RelayCommand]
    private void PointersToInstance()
    {
        if (SelectedInstance is { } i) context.FindPointersTo(i.Address);
    }
}

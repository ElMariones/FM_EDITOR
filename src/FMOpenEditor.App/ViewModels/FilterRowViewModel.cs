using CommunityToolkit.Mvvm.ComponentModel;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.App.ViewModels;

public sealed record OpOption(FilterOp Op, string Label)
{
    public static readonly IReadOnlyList<OpOption> All =
    [
        new(FilterOp.AtLeast, "at least"), new(FilterOp.AtMost, "at most"), new(FilterOp.Between, "between"),
        new(FilterOp.Equals, "is"), new(FilterOp.NotEquals, "is not"), new(FilterOp.Contains, "contains"),
        new(FilterOp.OneOf, "one of (a|b)"), new(FilterOp.IsEmpty, "is empty"), new(FilterOp.IsNotEmpty, "is set"),
    ];

    public override string ToString() => Label;
}

/// <summary>One line of Advanced Search: property, operator, value(s).</summary>
public sealed partial class FilterRowViewModel : ObservableObject
{
    private readonly EditorSession _session;

    public FilterRowViewModel(EditorSession session, IReadOnlyList<PropertyDef> properties)
    {
        _session = session;
        Properties = properties;
        _selectedProperty = properties.FirstOrDefault(p => p.Key == "Age") ?? properties.FirstOrDefault();
        _op = OpOption.All[0];
    }

    public IReadOnlyList<PropertyDef> Properties { get; }
    public IReadOnlyList<OpOption> Ops => OpOption.All;

    [ObservableProperty] private PropertyDef? _selectedProperty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NeedsSecondValue), nameof(NeedsValue))] private OpOption _op;
    [ObservableProperty] private string? _value;
    [ObservableProperty] private string? _value2;

    public bool NeedsValue => Op.Op is not (FilterOp.IsEmpty or FilterOp.IsNotEmpty);
    public bool NeedsSecondValue => Op.Op == FilterOp.Between;

    public string Hint => SelectedProperty?.Kind switch
    {
        ValueKind.Date => "yyyy-mm-dd",
        ValueKind.Reference => "name",
        ValueKind.Money => "e.g. 5M",
        ValueKind.Boolean => "yes / no",
        _ => "",
    };

    partial void OnSelectedPropertyChanged(PropertyDef? value) => OnPropertyChanged(nameof(Hint));

    /// <summary>Null when the row is incomplete. Throws <see cref="FormatException"/> for bad values.</summary>
    public Filter? ToFilter()
    {
        if (SelectedProperty is null) return null;
        if (!NeedsValue) return new Filter(SelectedProperty.Key, Op.Op);
        if (string.IsNullOrWhiteSpace(Value)) return null;
        object? Parse(string? t) => Op.Op is FilterOp.Contains or FilterOp.OneOf ? t : ValueParser.Parse(_session, SelectedProperty, t);
        return new Filter(SelectedProperty.Key, Op.Op, Parse(Value), NeedsSecondValue ? Parse(Value2) : null);
    }
}

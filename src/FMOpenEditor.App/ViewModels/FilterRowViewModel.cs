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

    /// <summary>Operators for a fixed-list field (Squad status, Continent…).</summary>
    public static readonly IReadOnlyList<OpOption> ForChoice = [All[3], All[4]];

    /// <summary>Position rows: "plays there" (rated 2+, no value) first, then the number operators.</summary>
    public static readonly OpOption Plays = new(FilterOp.AtLeast, "plays there");
    public static readonly IReadOnlyList<OpOption> ForPosition = [Plays, .. All.Take(5)];

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
        Categories = properties.Select(p => p.Category).Distinct().ToList();
        _selectedCategory = _selectedProperty?.Category;
    }

    public IReadOnlyList<PropertyDef> Properties { get; }

    /// <summary>The first box picks a category ("Profile", "Technical"...), the second a property in it.</summary>
    public IReadOnlyList<string> Categories { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CategoryProperties))] private string? _selectedCategory;
    public IReadOnlyList<PropertyDef> CategoryProperties => Properties.Where(p => p.Category == SelectedCategory).ToList();

    partial void OnSelectedCategoryChanged(string? value)
    {
        OnPropertyChanged(nameof(CategoryProperties));
        if (SelectedProperty?.Category != value) SelectedProperty = CategoryProperties.FirstOrDefault();
    }

    // The property box writes null while its list is swapped; keep the category then.
    // The value list box swaps its items too, so it gets its new list before its new pick.
    partial void OnSelectedPropertyChanged(PropertyDef? value)
    {
        if (value is null) return;
        SelectedCategory = value.Category;
        OnPropertyChanged(nameof(ValueChoices));
        SelectedValueChoice = ValueChoices.FirstOrDefault();
    }
    public IReadOnlyList<OpOption> Ops => OpOption.All;
    public IReadOnlyList<OpOption> ChoiceOps => OpOption.ForChoice;
    public IReadOnlyList<OpOption> PositionOps => OpOption.ForPosition;

    /// <summary>Position rows: "plays there" (rated 2+) or a number test on the 1-20 rating.</summary>
    public bool IsPosition => SelectedProperty?.Category == "Positions";
    /// <summary>Yes/no fields: one Yes / No box, no operator.</summary>
    public bool IsBool => SelectedProperty?.Kind == ValueKind.Boolean;
    /// <summary>Fixed-list fields: is / is not, then a box with the game's list.</summary>
    public bool IsChoice => SelectedProperty?.Kind == ValueKind.Choice && SelectedProperty.Choices is { Count: > 0 };
    public bool HasOp => !IsPosition && !IsBool && !IsChoice;
    private bool HasTextValue => HasOp || IsPosition && PositionOp != OpOption.Plays;
    private FilterOp CurrentOp => IsPosition ? PositionOp.Op : Op.Op;
    public bool HasValueChoices => IsBool || IsChoice;
    public IReadOnlyList<string> ValueChoices => IsBool ? ["Yes", "No"] : IsChoice ? SelectedProperty!.Choices! : [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Hint), nameof(IsPosition), nameof(IsBool), nameof(IsChoice), nameof(HasOp),
        nameof(HasValueChoices), nameof(NeedsValue), nameof(NeedsSecondValue))]
    private PropertyDef? _selectedProperty;
    [ObservableProperty] private OpOption _choiceOp = OpOption.ForChoice[0];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NeedsSecondValue), nameof(NeedsValue))] private OpOption _positionOp = OpOption.Plays;
    [ObservableProperty] private string? _selectedValueChoice;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(NeedsSecondValue), nameof(NeedsValue))] private OpOption _op;
    [ObservableProperty] private string? _value;
    [ObservableProperty] private string? _value2;

    public bool NeedsValue => HasTextValue && CurrentOp is not (FilterOp.IsEmpty or FilterOp.IsNotEmpty);
    public bool NeedsSecondValue => HasTextValue && CurrentOp == FilterOp.Between;

    public string Hint => SelectedProperty?.Kind switch
    {
        ValueKind.Date => "yyyy-mm-dd",
        ValueKind.Reference => "name",
        ValueKind.Money => "e.g. 5M",
        _ => "",
    };

    /// <summary>Null when the row is incomplete. Throws <see cref="FormatException"/> for bad values.</summary>
    public Filter? ToFilter()
    {
        if (SelectedProperty is null) return null;
        if (IsPosition && PositionOp == OpOption.Plays) return new Filter(SelectedProperty.Key, FilterOp.AtLeast, 2);
        if (IsBool) return SelectedValueChoice is null ? null : new Filter(SelectedProperty.Key, FilterOp.Equals, SelectedValueChoice == "Yes");
        if (IsChoice)
        {
            var i = SelectedProperty.Choices!.ToList().IndexOf(SelectedValueChoice ?? "");
            return i < 0 ? null : new Filter(SelectedProperty.Key, ChoiceOp.Op, i);
        }
        var op = CurrentOp;
        if (!NeedsValue) return new Filter(SelectedProperty.Key, op);
        if (string.IsNullOrWhiteSpace(Value)) return null;
        object? Parse(string? t) => op is FilterOp.Contains or FilterOp.OneOf ? t : ValueParser.Parse(_session, SelectedProperty, t);
        return new Filter(SelectedProperty.Key, op, Parse(Value), NeedsSecondValue ? Parse(Value2) : null);
    }
}

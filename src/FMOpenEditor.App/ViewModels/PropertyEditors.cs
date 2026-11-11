using CommunityToolkit.Mvvm.ComponentModel;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.ViewModels;

/// <summary>One row in the detail editor. Subclasses pick the control via DataTemplates.</summary>
public abstract partial class PropertyEditorViewModel(EditorSession session, Entity entity, PropertyDef def) : ObservableObject
{
    protected EditorSession Session { get; } = session;
    public Entity Entity { get; } = entity;
    public PropertyDef Def { get; } = def;

    public string Label => Def.Label;
    public string? Tooltip => Def.Description ?? Def.Key;
    public bool IsEditable => Session.CanEdit(Entity.Kind, Def.Key);

    // Attributes sit two to a row, so they get a narrower label and spinner.
    public double LabelWidth => Def.IsAttribute ? 96 : 160;
    public double EditorWidth => Def.IsAttribute ? 108 : 170;

    [ObservableProperty] private bool _isPending;
    [ObservableProperty] private string? _error;

    private bool _loading;

    public static PropertyEditorViewModel Create(EditorSession s, Entity e, PropertyDef d, Action<Entity>? open = null) => d switch
    {
        { IsDerived: true } => new ReadOnlyEditorViewModel(s, e, d),
        { Kind: ValueKind.Integer or ValueKind.Money or ValueKind.Decimal } => new NumberEditorViewModel(s, e, d),
        { Kind: ValueKind.Boolean } => new BoolEditorViewModel(s, e, d),
        { Kind: ValueKind.Date } => new DateEditorViewModel(s, e, d),
        { Kind: ValueKind.Choice } => new ChoiceEditorViewModel(s, e, d),
        { Kind: ValueKind.Reference } => new ReferenceEditorViewModel(s, e, d, open),
        _ => new TextEditorViewModel(s, e, d),
    };

    public void Load()
    {
        _loading = true;
        try
        {
            LoadValue(Session.TryRead(Entity, Def.Key));
            IsPending = Session.Changes.IsPending(Entity, Def.Key);
        }
        finally { _loading = false; }
    }

    protected abstract void LoadValue(object? value);

    protected void Commit(object? value)
    {
        if (_loading || !IsEditable) return;
        try
        {
            Session.Set(Entity, Def.Key, value);
            Error = null;
            IsPending = Session.Changes.IsPending(Entity, Def.Key);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}

public sealed partial class NumberEditorViewModel : PropertyEditorViewModel
{
    public NumberEditorViewModel(EditorSession s, Entity e, PropertyDef d) : base(s, e, d)
    {
        Minimum = (decimal)Math.Max(d.Min, -1e15);
        Maximum = (decimal)Math.Min(d.Max, 1e15);
        Increment = d.Kind == ValueKind.Money ? 100_000 : d.Max >= 1000 ? 100 : 1;
        FormatString = d.Kind == ValueKind.Decimal ? "0.##" : "N0";
        Load();
    }

    public decimal Minimum { get; }
    public decimal Maximum { get; }
    public decimal Increment { get; }
    public string FormatString { get; }
    public bool IsMoney => Def.Kind == ValueKind.Money;

    [ObservableProperty] private decimal? _value;

    protected override void LoadValue(object? v) => Value = v is null ? null : (decimal)Values.ToDouble(v);
    partial void OnValueChanged(decimal? value)
    {
        if (value is not null) Commit(value);
        OnPropertyChanged(nameof(MoneyText));
    }

    public string MoneyText => Value is { } v && IsMoney ? Values.FormatMoney((long)v) : "";
}

public sealed partial class BoolEditorViewModel : PropertyEditorViewModel
{
    public BoolEditorViewModel(EditorSession s, Entity e, PropertyDef d) : base(s, e, d) => Load();

    [ObservableProperty] private bool _isChecked;

    protected override void LoadValue(object? v) => IsChecked = v is true;
    partial void OnIsCheckedChanged(bool value) => Commit(value);
}

public sealed partial class TextEditorViewModel : PropertyEditorViewModel
{
    public TextEditorViewModel(EditorSession s, Entity e, PropertyDef d) : base(s, e, d) => Load();

    [ObservableProperty] private string? _text;

    protected override void LoadValue(object? v) => Text = v as string ?? "";
    partial void OnTextChanged(string? value) => Commit(value ?? "");
}

public sealed partial class DateEditorViewModel : PropertyEditorViewModel
{
    public DateEditorViewModel(EditorSession s, Entity e, PropertyDef d) : base(s, e, d) => Load();

    [ObservableProperty] private DateTime? _date;

    protected override void LoadValue(object? v) => Date = v is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : null;
    partial void OnDateChanged(DateTime? value) => Commit(value is { } dt ? DateOnly.FromDateTime(dt) : null);
}

public sealed partial class ChoiceEditorViewModel : PropertyEditorViewModel
{
    public ChoiceEditorViewModel(EditorSession s, Entity e, PropertyDef d) : base(s, e, d) => Load();

    public IReadOnlyList<string> Choices => Def.Choices ?? [];

    [ObservableProperty] private int _selectedIndex = -1;

    protected override void LoadValue(object? v) => SelectedIndex = v is int i ? i : -1;
    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0) Commit(value);
    }
}

public sealed record RefOption(int? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed partial class ReferenceEditorViewModel : PropertyEditorViewModel
{
    private readonly Action<Entity>? _open;

    public ReferenceEditorViewModel(EditorSession s, Entity e, PropertyDef d, Action<Entity>? open = null) : base(s, e, d)
    {
        _open = open;
        var kind = d.RefKind!.Value;
        Options = [new RefOption(null, "— none —"),
                   .. s.GetAll(kind).Select(x => new RefOption(x.Id, s.NameOf(kind, x.Id))).OrderBy(o => o.Name)];
        Load();
    }

    public IReadOnlyList<RefOption> Options { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanOpen), nameof(OpenTip))] [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private RefOption? _selected;

    /// <summary>Whether there is something to jump to (a value, and a page to show it in).</summary>
    public bool CanOpen => _open is not null && Selected?.Id is not null;
    public bool ShowOpen => _open is not null;
    public string OpenTip => Selected?.Id is null ? "" : $"Open {Selected.Name}";

    protected override void LoadValue(object? v) => Selected = Options.FirstOrDefault(o => o.Id == v as int?) ?? Options[0];
    partial void OnSelectedChanged(RefOption? value)
    {
        if (value is not null) Commit(value.Id);
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        if (Selected?.Id is { } id && Session.Database.Find(Def.RefKind!.Value, id) is { } target) _open?.Invoke(target);
    }
}

public sealed partial class ReadOnlyEditorViewModel : PropertyEditorViewModel
{
    public ReadOnlyEditorViewModel(EditorSession s, Entity e, PropertyDef d) : base(s, e, d) => Load();

    [ObservableProperty] private string _display = "";

    protected override void LoadValue(object? v) => Display = Values.Format(Def, v, Session.NameOf);
}

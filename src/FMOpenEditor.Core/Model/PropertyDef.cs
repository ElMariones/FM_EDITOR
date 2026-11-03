namespace FMOpenEditor.Core.Model;

/// <summary>How a property's value is represented in .NET.</summary>
public enum ValueKind
{
    /// <summary><see cref="int"/>.</summary>
    Integer,
    /// <summary><see cref="long"/>, in the game's base currency unit.</summary>
    Money,
    /// <summary><see cref="double"/>.</summary>
    Decimal,
    /// <summary><see cref="bool"/>.</summary>
    Boolean,
    /// <summary><see cref="string"/>.</summary>
    Text,
    /// <summary><see cref="DateOnly"/>, or null when unset.</summary>
    Date,
    /// <summary><see cref="int"/> index into <see cref="PropertyDef.Choices"/>.</summary>
    Choice,
    /// <summary><see cref="int"/> id of another entity of <see cref="PropertyDef.RefKind"/>, or null for none.</summary>
    Reference,
}

/// <summary>
/// One editable (or derived) property of an entity. The catalog of these is the single source of
/// truth for the UI, search, presets and mass edit: adding a property here and its offset to a game
/// definition is all it takes to make it editable everywhere.
/// </summary>
public sealed record PropertyDef(string Key, string Label, string Category, ValueKind Kind)
{
    public double Min { get; init; } = int.MinValue;
    public double Max { get; init; } = int.MaxValue;
    public EntityKind? RefKind { get; init; }
    public IReadOnlyList<string>? Choices { get; init; }

    /// <summary>Computed from other properties (e.g. Age); never written.</summary>
    public bool IsDerived { get; init; }

    /// <summary>Part of the attribute set that Freeze / Only-up and Compare work on.</summary>
    public bool IsAttribute { get; init; }

    public string? Description { get; init; }

    public bool IsNumeric => Kind is ValueKind.Integer or ValueKind.Money or ValueKind.Decimal;
}

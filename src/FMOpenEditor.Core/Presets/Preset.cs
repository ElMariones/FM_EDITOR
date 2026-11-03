using System.Text.Json;
using System.Text.Json.Serialization;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Search;

namespace FMOpenEditor.Core.Presets;

public enum PresetOp
{
    /// <summary>Replace the value.</summary>
    Set,
    /// <summary>Add to a number (negative to subtract).</summary>
    Add,
    /// <summary>Multiply a number.</summary>
    Multiply,
    /// <summary>Raise to at least the value; never lowers.</summary>
    AtLeast,
    /// <summary>Lower to at most the value; never raises.</summary>
    AtMost,
}

public enum PresetScope
{
    /// <summary>The rows the user picked.</summary>
    Selection,
    /// <summary>Every entity of the target kind that matches <see cref="Preset.Where"/>.</summary>
    All,
}

/// <summary>
/// One step of a preset. Exactly one of <see cref="Property"/> or <see cref="Custom"/> is set.
/// <see cref="Property"/> may end in <c>*</c> to match every key with that prefix (e.g. <c>Attr.*</c>),
/// optionally narrowed by <see cref="Category"/>.
/// </summary>
public sealed class PresetAction
{
    public string? Property { get; set; }
    public string? Category { get; set; }
    public PresetOp Op { get; set; } = PresetOp.Set;
    public object? Value { get; set; }

    /// <summary>Copy the value of another property of the same entity instead of a constant.</summary>
    public string? FromProperty { get; set; }

    /// <summary>Name of a registered <see cref="ICustomAction"/>.</summary>
    public string? Custom { get; set; }
    public string? Parameter { get; set; }
}

/// <summary>A named, shareable set of edits, stored as JSON in the presets folder.</summary>
public sealed class Preset
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public EntityKind Target { get; set; } = EntityKind.Player;
    public string Category { get; set; } = "General";
    public int Order { get; set; } = 100;

    /// <summary>Show as a one-click button in the toolbar.</summary>
    public bool Quick { get; set; }

    public PresetScope Scope { get; set; } = PresetScope.Selection;

    /// <summary>Only entities matching every filter are changed.</summary>
    public List<Filter> Where { get; set; } = [];

    public List<PresetAction> Actions { get; set; } = [];

    [JsonIgnore] public string? SourcePath { get; set; }
    [JsonIgnore] public bool IsBuiltIn { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Preset Load(string path)
    {
        var p = JsonSerializer.Deserialize<Preset>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"{path} is empty.");
        p.SourcePath = path;
        return p;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
}

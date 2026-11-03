using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FMOpenEditor.Core.Definitions;

/// <summary>
/// Describes where the game keeps its data for one game build. Definitions are plain JSON files in
/// <c>definitions/</c> so that supporting a new patch is a pull request with numbers, not code.
/// See docs/DEFINITIONS.md for the format.
/// </summary>
public sealed class GameDefinition
{
    public string Game { get; set; } = "";
    public string Version { get; set; } = "";
    public string Module { get; set; } = Memory.GameProcess.DefaultModule;

    /// <summary>File versions of <see cref="Module"/> this definition was mapped against.</summary>
    public List<string> ModuleVersions { get; set; } = [];

    /// <summary>False until someone has confirmed the offsets against the real game.</summary>
    public bool Verified { get; set; }
    public string? Notes { get; set; }

    public Dictionary<string, RootDef> Roots { get; set; } = [];

    /// <summary>Keyed by entity kind name (Player, Staff, Club, Competition, Nation).</summary>
    public Dictionary<string, CollectionDef> Collections { get; set; } = [];

    /// <summary>Keyed by entity kind name, then by property key from the catalog.</summary>
    public Dictionary<string, Dictionary<string, FieldDef>> Structs { get; set; } = [];

    /// <summary>The in-game current date (a field read relative to a root).</summary>
    public FieldDef? GameDate { get; set; }
    public string? GameDateRoot { get; set; }

    [JsonIgnore] public string? SourcePath { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static GameDefinition Load(string path)
    {
        var def = JsonSerializer.Deserialize<GameDefinition>(File.ReadAllText(path), JsonOptions)
                  ?? throw new InvalidDataException($"{path} is empty.");
        def.SourcePath = path;
        return def;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>Parses "0x1A0", "1A0h" or "416".</summary>
    public static long ParseOffset(string text)
    {
        var t = text.Trim();
        var negative = t.StartsWith('-');
        if (negative) t = t[1..];
        long v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : t.EndsWith('h') ? long.Parse(t[..^1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(t, CultureInfo.InvariantCulture);
        return negative ? -v : v;
    }
}

/// <summary>A global pointer found either by signature or by a fixed module-relative offset.</summary>
public sealed class RootDef
{
    /// <summary>Byte signature of an instruction that references the global, e.g. a RIP-relative MOV.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Pattern { get; set; }
    public int DispOffset { get; set; } = 3;
    public int InstructionLength { get; set; } = 7;

    /// <summary>Alternative to <see cref="Pattern"/>: offset of the global from the module base.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ModuleOffset { get; set; }

    /// <summary>Pointer hops after locating the global: read pointer at (current + offset), repeat.</summary>
    public List<string> Deref { get; set; } = [];

    public string? Description { get; set; }
}

public enum CollectionLayout
{
    /// <summary>std::vector-like: pointer to first element and pointer past the last; elements are pointers to records.</summary>
    PointerRange,
    /// <summary>Pointer to an array of record pointers, plus an int32 count.</summary>
    PointerArray,
    /// <summary>Pointer to records stored inline, plus a count and a fixed stride.</summary>
    InlineArray,
}

public sealed class CollectionDef
{
    public string Root { get; set; } = "";
    public CollectionLayout Layout { get; set; } = CollectionLayout.PointerRange;
    public string BeginOffset { get; set; } = "0x0";
    public string EndOffset { get; set; } = "0x8";
    public string? CountOffset { get; set; }
    public int Stride { get; set; }

    /// <summary>
    /// When players and staff share one "people" table, a field that tells them apart.
    /// The record is included when that field reads <see cref="FilterValue"/>.
    /// </summary>
    public FieldDef? Filter { get; set; }
    public long FilterValue { get; set; }

    /// <summary>Upper bound to guard against reading garbage if a definition is wrong.</summary>
    public int MaxCount { get; set; } = 2_000_000;
}

public enum FieldType
{
    U8, I8, U16, I16, I32, U32, I64, F32, F64, Bool,
    /// <summary>FM native date: u16 day of year (0-based) then u16 year.</summary>
    FmDate,
    /// <summary>Pointer to a null-terminated UTF-16 string.</summary>
    Utf16Ptr,
    /// <summary>Pointer to a null-terminated UTF-8 string.</summary>
    Utf8Ptr,
    /// <summary>UTF-16 characters stored inside the record (<see cref="FieldDef.MaxLength"/> chars).</summary>
    Utf16Inline,
    /// <summary>Pointer to another entity's record; the value is that entity's id.</summary>
    EntityPtr,
}

public sealed class FieldDef
{
    /// <summary>Offset from the record base (after following <see cref="Path"/>). Null = not mapped yet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Offset { get; set; }
    public FieldType Type { get; set; } = FieldType.I32;

    /// <summary>Pointer hops from the record base before applying <see cref="Offset"/>.</summary>
    public List<string>? Path { get; set; }

    /// <summary>Raw = shown × Scale. FM stores most 1-20 attributes as 1-100, i.e. scale 5.</summary>
    public double Scale { get; set; } = 1;

    public int MaxLength { get; set; } = 64;

    /// <summary>Set by whoever verified this field in-game.</summary>
    public bool Verified { get; set; }

    [JsonIgnore] public bool IsMapped => !string.IsNullOrWhiteSpace(Offset);
}

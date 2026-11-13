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

    /// <summary>
    /// What the day-of-year half of an <see cref="FieldType.FmDate"/> counts from: 0 (1 January = 0) or
    /// 1 (1 January = 1, as in FM27). With 1, the game's empty date (day 1 of 1900) reads as no date.
    /// </summary>
    public int DateDayBase { get; set; }

    public Dictionary<string, RootDef> Roots { get; set; } = [];

    /// <summary>Keyed by entity kind name (Player, Staff, Club, Competition, Nation).</summary>
    public Dictionary<string, CollectionDef> Collections { get; set; } = [];

    /// <summary>Keyed by entity kind name, then by property key from the catalog.</summary>
    public Dictionary<string, Dictionary<string, FieldDef>> Structs { get; set; } = [];

    /// <summary>The in-game current date (a field read relative to a root).</summary>
    public FieldDef? GameDate { get; set; }
    public string? GameDateRoot { get; set; }

    /// <summary>
    /// The game's currency choice: a preferences file under %LOCALAPPDATA% holding a number, which is an
    /// index into <see cref="CurrencyPreferenceIds"/> (a list in the game's code) giving the currency id.
    /// </summary>
    public string? CurrencyPreferenceFile { get; set; }
    public List<int>? CurrencyPreferenceIds { get; set; }

    /// <summary>The currency id the game shows amounts in, or null when its preferences file can't be read.</summary>
    public int? ReadGameCurrencyId()
    {
        if (CurrencyPreferenceFile is null || CurrencyPreferenceIds is not { Count: > 0 } ids) return null;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CurrencyPreferenceFile);
            var value = System.Xml.Linq.XDocument.Load(path).Descendants("integer").FirstOrDefault()?.Attribute("value")?.Value;
            return int.TryParse(value, out var i) && i >= 0 && i < ids.Count ? ids[i] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { return null; }
    }

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
    /// <summary>
    /// Every live object of the listed C++ classes, found by scanning the heap for their vtables
    /// (located by RTTI class name). Needs no root and survives patches that move globals.
    /// </summary>
    RttiInstances,
}

/// <summary>One class an <see cref="CollectionLayout.RttiInstances"/> collection draws records from.</summary>
public sealed class RttiSourceDef
{
    /// <summary>Demangled RTTI name, e.g. <c>db::ACTUAL_PLAYER</c>.</summary>
    public string Class { get; set; } = "";

    /// <summary>
    /// Which of the class's vtables to look for, by its offset inside the object (0 = primary). The
    /// record base is that vtable's position, so choosing the vtable of a shared base part (such as
    /// db::PERSON) gives every class the same field offsets.
    /// </summary>
    public string VtableOffset { get; set; } = "0x0";

    /// <summary>True for the class of human-controlled managers: their clubs are listed as "My club".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Human { get; set; }
}

public sealed class CollectionDef
{
    /// <summary>Root the table hangs off. Not used by <see cref="CollectionLayout.RttiInstances"/>.</summary>
    public string Root { get; set; } = "";

    /// <summary>For <see cref="CollectionLayout.RttiInstances"/>: the classes whose objects are records.</summary>
    public List<RttiSourceDef>? Classes { get; set; }
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
    /// <summary>FM native date: u16 day of year then u16 year (see <see cref="GameDefinition.DateDayBase"/>).</summary>
    FmDate,
    /// <summary>Pointer to a null-terminated UTF-16 string.</summary>
    Utf16Ptr,
    /// <summary>Pointer to a null-terminated UTF-8 string.</summary>
    Utf8Ptr,
    /// <summary>UTF-16 characters stored inside the record (<see cref="FieldDef.MaxLength"/> chars).</summary>
    Utf16Inline,
    /// <summary>Pointer to another entity's record; the value is that entity's id.</summary>
    EntityPtr,
    /// <summary>Pointer to FM's length-prefixed string: an i32 byte count, then that many UTF-8 bytes and a 0.</summary>
    FmString,
    /// <summary>
    /// A flag the game keeps as a std::vector of pointers (begin at the offset, end 8 bytes later):
    /// true while any element has a non-zero counter at <see cref="FieldDef.ElementOffset"/>
    /// (<see cref="FieldDef.ElementBytes"/> wide). Writing false zeroes those counters and lets the
    /// game treat the list as empty (FM27 shows the player fit by the next day). It can't be set to true.
    /// </summary>
    ListFlag,
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

    /// <summary>
    /// Shown but never written: a link the game keeps elsewhere too (a club's league is also in the
    /// competition's own team list), so changing only this copy would leave the save inconsistent.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReadOnly { get; set; }

    /// <summary>
    /// For a <see cref="FieldType.Bool"/> packed into a flags byte: the bit that holds it (e.g. "0x10").
    /// Reading tests the bit; writing changes only that bit.
    /// </summary>
    public string? Mask { get; set; }

    /// <summary>
    /// For a choice whose stored numbers are not 0, 1, 2…: the stored number of each choice, in the order
    /// of the property's choice list (e.g. staff jobs are stored as 0, 2, 4, 6…). A stored number that is
    /// not listed reads as empty.
    /// </summary>
    public List<long>? Values { get; set; }

    /// <summary>
    /// For one bonus or clause of a contract: the type byte (e.g. "0x21" goal bonus). The offset then points
    /// at a std::vector of 8-byte entries { i32 amount; i16 count; u8 type; u8 pad }; the value is read at
    /// <see cref="ElementOffset"/> of the entry with this type, -1 = not used. A contract without this
    /// clause reads empty and can't be written (adding an entry would need memory in the game).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Clause { get; set; }

    /// <summary>For <see cref="FieldType.ListFlag"/>: where each element's counter sits.</summary>
    public string? ElementOffset { get; set; }

    /// <summary>For <see cref="FieldType.ListFlag"/>: how many counter bytes to test and clear.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ElementBytes { get; set; }

    /// <summary>Set by whoever verified this field in-game.</summary>
    public bool Verified { get; set; }

    [JsonIgnore] public bool IsMapped => !string.IsNullOrWhiteSpace(Offset);
}

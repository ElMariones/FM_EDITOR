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

    /// <summary>A human manager's team selections, emptied of a player who leaves the club. Optional.</summary>
    public SelectionDef? Selections { get; set; }

    /// <summary>The game's pool of player contracts, from which a free agent's contract is taken. Optional.</summary>
    public ContractPoolDef? ContractPool { get; set; }

    /// <summary>
    /// How the game prices a player's attributes into current ability, keyed by position property
    /// ("Pos.ST"). Optional; without it the editor falls back to a plain average.
    /// </summary>
    public Dictionary<string, AbilityWeightsDef> AbilityWeights { get; set; } = [];

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
    /// For an <see cref="FieldType.EntityPtr"/> that is read through a chain but must be written one link
    /// earlier: where the pointer is stored. A player's club is read as contract → team → club, but a
    /// transfer changes the contract's team, not the team's club (that would move the whole team).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FieldLocation? StoreAt { get; set; }

    /// <summary>
    /// Pointer hops from the chosen record to what is stored at <see cref="StoreAt"/> (e.g. from a club to
    /// its first team: ["0x18", "0x0"]). Empty means the record itself.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StoreTarget { get; set; }

    /// <summary>
    /// Other copies of the same link (a player's squad team, the contract's second team pointer). Each is
    /// moved to the new target when it pointed at the old one or one of its siblings, and left alone
    /// otherwise (a national team, say).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<FieldLocation>? AlsoStoreAt { get; set; }

    /// <summary>Squad lists to keep in step when the stored pointer changes; see <see cref="MemberListDef"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MemberListDef? Members { get; set; }

    /// <summary>When set, the field is one keyed entry of a list at <see cref="Offset"/> (see <see cref="ListEntryDef"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ListEntryDef? Entry { get; set; }

    /// <summary>
    /// Shorthand for an <see cref="Entry"/> in a contract's bonus and clause list: the kind byte (e.g. "0x21"
    /// goal bonus). The list holds 8-byte entries { i32 amount; i16 count or percent; u8 kind; u8 pad };
    /// the value is read at <see cref="ElementOffset"/> ("0x0" for money, "0x4" for a percent or count) and
    /// -1 means not used.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Clause { get; set; }

    /// <summary>The list entry this field reads: <see cref="Entry"/>, or the one <see cref="Clause"/> stands for.</summary>
    [JsonIgnore]
    public ListEntryDef? ListEntry => Entry ?? (Clause is null ? null : _clauseEntry ??= new ListEntryDef
    {
        Stride = 8,
        KeyOffset = "0x6",
        KeyType = FieldType.U8,
        Key = GameDefinition.ParseOffset(Clause),
        ValueOffset = ElementOffset ?? "0x0",
        NoValue = -1,
    });
    private ListEntryDef? _clauseEntry;

    /// <summary>For <see cref="FieldType.ListFlag"/>, where each element's counter sits; for a <see cref="Clause"/>, where its value sits.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ElementOffset { get; set; }

    /// <summary>For <see cref="FieldType.ListFlag"/>: how many counter bytes to test and clear.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ElementBytes { get; set; }

    /// <summary>Set by whoever verified this field in-game.</summary>
    public bool Verified { get; set; }

    [JsonIgnore] public bool IsMapped => !string.IsNullOrWhiteSpace(Offset);
}

/// <summary>A place in a record: pointer hops, then an offset.</summary>
public sealed class FieldLocation
{
    public List<string>? Path { get; set; }
    public string Offset { get; set; } = "0x0";
}

/// <summary>
/// Lists of record pointers (begin, end, capacity) that name the record as a member, such as a team's
/// squad. When a stored link moves from one owner to another, the record is taken out of every list
/// reachable from the old owner and added to the new owner's list.
/// </summary>
public sealed class MemberListDef
{
    /// <summary>Offset of the list in each owner (a team's squad: "0x38").</summary>
    public string List { get; set; } = "0x0";

    /// <summary>Pointer hops from the old owner to the object holding its siblings (team → club: ["0x30"]).</summary>
    public List<string>? SiblingsPath { get; set; }

    /// <summary>Offset of the sibling list in that object (a club's teams: "0x18"). Without it only the old owner is searched.</summary>
    public string? Siblings { get; set; }

    /// <summary>
    /// Entity kind whose records carry the same list (e.g. "Team"). When the new owner's list is full,
    /// its storage is swapped with a roomier one of these instead of growing it.
    /// </summary>
    public string? Donors { get; set; }

    /// <summary>
    /// The game allocator's block sizes in bytes, smallest first. Its blocks have no headers, so a list
    /// whose capacity ends short of its block's size has unused room after it, which reads as zeros.
    /// A full list takes the new member there before any donor is looked for. Only capacities above
    /// the first size and up to the last one are trusted.
    /// </summary>
    public List<int>? BlockSizes { get; set; }
}

/// <summary>
/// One entry of a list of fixed-size entries (begin, end, capacity) at the field's offset, picked by a
/// key: a contract's release clause is the clause whose kind is 0. Reading gives the entry's value,
/// or null when there is none. Writing changes the value in place; writing null or 0 takes the entry
/// out. A missing entry can't be added, as that would need room the list may not have.
/// </summary>
public sealed class ListEntryDef
{
    public int Stride { get; set; } = 8;
    public string KeyOffset { get; set; } = "0x0";
    public FieldType KeyType { get; set; } = FieldType.U16;
    public long Key { get; set; }
    public string ValueOffset { get; set; } = "0x0";

    /// <summary>The stored value meaning "none" (FM: 0xFFFFFFFF), read as null.</summary>
    public long? NoValue { get; set; }
}

/// <summary>How the game prices current ability at one position (see <see cref="GameDefinition.AbilityWeights"/>).</summary>
public sealed class AbilityWeightsDef
{
    /// <summary>Current ability of a player at this position is Const + the sum of weight × displayed value (1–20).</summary>
    public double Const { get; set; }
    public Dictionary<string, double> Weights { get; set; } = [];
}

/// <summary>
/// Objects holding a team selection: a list (begin, end, capacity) that is sound in live copies, then
/// <see cref="Count"/> u32 person ids, 0xFFFFFFFF for an empty slot.
/// </summary>
public sealed class SelectionDef
{
    public string Class { get; set; } = "";
    public string List { get; set; } = "0x8";
    public string Slots { get; set; } = "0x28";
    public int Count { get; set; } = 26;

    /// <summary>Offset of the u32 person id in a player record (the person block).</summary>
    public string PersonId { get; set; } = "0x8";
}

/// <summary>
/// A pool of fixed-size objects in the module's writable data: (begin, end, capacity) of chunk
/// pointers, then the first free slot (each free slot's first 8 bytes point at the next), then u32
/// free and used counts, u32 slot size. A new contract is a copy of a team-mate's, made the player's.
/// </summary>
public sealed class ContractPoolDef
{
    public string Class { get; set; } = "";
    public int Size { get; set; } = 200;
    public string Owner { get; set; } = "0x8";
    public List<string> Teams { get; set; } = [];
    public string Job { get; set; } = "0x26";
    public int PlayerJob { get; set; } = 1;
    public string SquadStatus { get; set; } = "0x54";
    public List<long> KeeperStatuses { get; set; } = [];
    public List<string> Dates { get; set; } = [];
    public string? SquadNumber { get; set; }
    public string? Listing { get; set; }

    /// <summary>Qwords set to zero: pointers and lists that must not be shared with the template.</summary>
    public List<string> Cleared { get; set; } = [];
}

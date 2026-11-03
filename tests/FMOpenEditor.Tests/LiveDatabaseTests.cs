using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Tests;

/// <summary>
/// Builds a tiny fake game image (module with a signature, a people table, a club table) and checks
/// the definition-driven reader finds and edits everything through it.
/// </summary>
public class LiveDatabaseTests
{
    private const nint Module = 0x10000;
    private const nint Heap = 0x20000;

    private static (FakeMemory Mem, LiveDatabase Db) Build()
    {
        var m = new FakeMemory().Map(Module, 0x1000, writable: false).Map(Heap, 0x10000);

        // mov rax, [rip+disp] ; test rax, rax   -> global at Module+0x800
        m.Bytes(Module + 0x100, 0x48, 0x8B, 0x05);
        m.I32(Module + 0x103, 0x800 - (0x100 + 7));
        m.Bytes(Module + 0x107, 0x48, 0x85, 0xC0);
        m.I64(Module + 0x800, Heap);            // People global -> table object
        m.I64(Module + 0x900, Heap + 0x100);    // Clubs global -> table object
        m.I16(Module + 0xA00, 181);             // game date: day of year (0-based)
        m.I16(Module + 0xA02, 2026);

        // People table: std::vector of 3 pointers.
        m.I64(Heap, Heap + 0x1000);
        m.I64(Heap + 8, Heap + 0x1000 + 3 * 8);
        nint[] people = [Heap + 0x2000, Heap + 0x2100, Heap + 0x2200];
        for (var i = 0; i < people.Length; i++) m.I64(Heap + 0x1000 + i * 8, people[i]);

        // Club table: pointer array + count.
        m.I64(Heap + 0x100, Heap + 0x1100);
        m.I32(Heap + 0x108, 1);
        nint club = Heap + 0x2300;
        m.I64(Heap + 0x1100, club);
        m.I32(club, 500);
        m.I64(club + 8, Heap + 0x3300);
        m.Utf16(Heap + 0x3300, "Testville FC");

        void Person(nint rec, int id, byte type, string first, string last, byte finishingRaw, nint contract)
        {
            m.I32(rec, id);
            m.Bytes(rec + 4, type);
            m.I64(rec + 8, rec + 0x1000);
            m.Utf16(rec + 0x1000, first);
            m.I64(rec + 0x10, rec + 0x1040);
            m.Utf16(rec + 0x1040, last);
            m.I16(rec + 0x18, 0);    // 1 Jan
            m.I16(rec + 0x1A, 2008);
            m.Bytes(rec + 0x40, finishingRaw);
            m.I64(rec + 0x50, contract);
        }
        Person(people[0], 1, 1, "Ana", "Pérez", 75, Heap + 0x4000);
        Person(people[1], 2, 2, "Coach", "Staffer", 0, Heap + 0x4100);
        Person(people[2], 3, 1, "Ben", "Long", 50, Heap + 0x4200);
        m.I64(Heap + 0x4000, club);
        m.I64(Heap + 0x4008, 12_000);

        FieldDef F(string offset, FieldType type, double scale = 1, params string[] path) =>
            new() { Offset = offset, Type = type, Scale = scale, Path = path.Length > 0 ? [.. path] : null };

        var personFields = new Dictionary<string, FieldDef>
        {
            ["Id"] = F("0x0", FieldType.I32),
            ["FirstName"] = F("0x8", FieldType.Utf16Ptr),
            ["LastName"] = F("0x10", FieldType.Utf16Ptr),
            ["DateOfBirth"] = F("0x18", FieldType.FmDate),
            ["Attr.Finishing"] = F("0x40", FieldType.U8, 5),
            ["Contract.Club"] = F("0x0", FieldType.EntityPtr, 1, "0x50"),
            ["Contract.Wage"] = F("0x8", FieldType.I64, 1, "0x50"),
        };

        var def = new GameDefinition
        {
            Version = "test",
            Roots =
            {
                ["People"] = new RootDef { Pattern = "48 8B 05 ?? ?? ?? ?? 48 85 C0", Deref = ["0x0"] },
                ["Clubs"] = new RootDef { ModuleOffset = "0x900", Deref = ["0x0"] },
                ["Clock"] = new RootDef { ModuleOffset = "0xA00" },
            },
            Collections =
            {
                ["Player"] = new CollectionDef { Root = "People", Filter = F("0x4", FieldType.U8), FilterValue = 1 },
                ["Staff"] = new CollectionDef { Root = "People", Filter = F("0x4", FieldType.U8), FilterValue = 2 },
                ["Club"] = new CollectionDef { Root = "Clubs", Layout = CollectionLayout.PointerArray, CountOffset = "0x8" },
            },
            Structs =
            {
                ["Player"] = personFields,
                ["Staff"] = personFields,
                ["Club"] = new() { ["Id"] = F("0x0", FieldType.I32), ["Name"] = F("0x8", FieldType.Utf16Ptr) },
            },
            GameDate = F("0x0", FieldType.FmDate),
            GameDateRoot = "Clock",
        };

        return (m, new LiveDatabase(m, def, Module, 0x1000, "fake"));
    }

    [Fact]
    public void Finds_records_through_signature_and_filters_people()
    {
        var (_, db) = Build();
        Assert.Empty(db.Warnings);
        Assert.Equal([1, 3], db.GetAll(EntityKind.Player).Select(e => e.Id));
        Assert.Equal([2], db.GetAll(EntityKind.Staff).Select(e => e.Id));
        Assert.Equal(500, Assert.Single(db.GetAll(EntityKind.Club)).Id);
        Assert.Equal(new DateOnly(2026, 7, 1), db.GameDate);
    }

    [Fact]
    public void Reads_typed_values_with_scale_paths_and_references()
    {
        var (_, db) = Build();
        var ana = db.Find(EntityKind.Player, 1)!;
        Assert.Equal("Ana", db.Read(ana, "FirstName"));
        Assert.Equal("Pérez", db.Read(ana, "LastName"));
        Assert.Equal(new DateOnly(2008, 1, 1), db.Read(ana, "DateOfBirth"));
        Assert.Equal(15, db.Read(ana, "Attr.Finishing"));   // 75 / 5
        Assert.Equal(500, db.Read(ana, "Contract.Club"));
        Assert.Equal(12_000L, db.Read(ana, "Contract.Wage"));
        Assert.Equal("Testville FC", db.Read(db.Find(EntityKind.Club, 500)!, "Name"));
        Assert.False(db.Supports(EntityKind.Player, "Attr.Pace"));
    }

    [Fact]
    public void Writes_go_to_the_right_bytes()
    {
        var (m, db) = Build();
        var ana = db.Find(EntityKind.Player, 1)!;
        db.Write(ana, "Attr.Finishing", 20);
        db.Write(ana, "Contract.Wage", 99_000L);
        db.Write(ana, "DateOfBirth", new DateOnly(2009, 3, 1));
        db.Write(ana, "LastName", "Paz");

        Assert.Equal(100, m.ReadByte(Heap + 0x2040));
        Assert.Equal(99_000, m.ReadInt64(Heap + 0x4008));
        Assert.Equal(new DateOnly(2009, 3, 1), db.Read(ana, "DateOfBirth"));
        Assert.Equal("Paz", db.Read(ana, "LastName"));
        Assert.Throws<NotSupportedException>(() => db.Write(ana, "LastName", "Much longer name"));

        db.Write(ana, "Contract.Club", null);
        Assert.Null(db.Read(ana, "Contract.Club"));
    }

    [Fact]
    public void Signature_scanner_handles_wildcards_and_misses()
    {
        var (m, _) = Build();
        Assert.Equal(Module + 0x100, SignatureScanner.Find(m, Module, 0x1000, "48 8B 05 ? ? ? ? 48 85 C0"));
        Assert.Null(SignatureScanner.Find(m, Module, 0x1000, "DE AD BE EF"));
    }

    [Fact]
    public void Memory_scanner_narrows_to_the_changed_address()
    {
        var (m, _) = Build();
        var scanner = new MemoryScanner(m);
        Assert.True(scanner.FirstScan(12_000, ScanValueType.Int32) >= 1);
        m.I64(Heap + 0x4008, 13_500);
        Assert.Equal(1, scanner.NextScan(ScanCompare.Increased));
        Assert.Equal(Heap + 0x4008, scanner.Results[0]);
    }

    [Fact]
    public void Missing_roots_become_warnings_not_crashes()
    {
        var m = new FakeMemory().Map(Module, 0x1000);
        var def = DefinitionLibrary.CreateTemplate("FM", "27.0.0");
        var db = new LiveDatabase(m, def, Module, 0x1000, "empty");
        Assert.NotEmpty(db.Warnings);
        Assert.Empty(db.GetAll(EntityKind.Player));
    }
}

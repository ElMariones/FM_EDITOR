using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Tests;

public class BitFlagAndCatalogTests
{
    private const nint Module = 0x10000;
    private const nint Heap = 0x20000;

    [Fact]
    public void A_masked_bool_reads_and_writes_only_its_own_bit()
    {
        var m = new FakeMemory().Map(Module, 0x1000, writable: false).Map(Heap, 0x1000);
        nint nation = Heap + 0x100;
        m.I64(Module + 0x800, Heap);     // Nations global -> table object
        m.I64(Heap, Heap + 0x80);        // pointer array
        m.I32(Heap + 8, 1);              // count
        m.I64(Heap + 0x80, nation);
        m.I32(nation, 7);
        m.Bytes(nation + 0x10, 0b1010_0101);

        var def = new GameDefinition
        {
            Roots = { ["Nations"] = new RootDef { ModuleOffset = "0x800", Deref = ["0x0"] } },
            Collections = { ["Nation"] = new CollectionDef { Root = "Nations", Layout = CollectionLayout.PointerArray, CountOffset = "0x8" } },
            Structs =
            {
                ["Nation"] = new()
                {
                    ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 },
                    ["Rules.EuFreeMovement"] = new FieldDef { Offset = "0x10", Type = FieldType.Bool, Mask = "0x10" },
                    ["Rules.WorkPermitsForNonEu"] = new FieldDef { Offset = "0x10", Type = FieldType.Bool, Mask = "0x04" },
                },
            },
        };
        using var db = new LiveDatabase(m, def, Module, 0x1000, "test");
        var e = Assert.Single(db.GetAll(EntityKind.Nation));

        Assert.Equal(false, db.Read(e, "Rules.EuFreeMovement"));
        Assert.Equal(true, db.Read(e, "Rules.WorkPermitsForNonEu"));

        db.Write(e, "Rules.EuFreeMovement", true);
        db.Write(e, "Rules.WorkPermitsForNonEu", false);

        // Bits 0x10 and 0x04 flipped; the other six are untouched.
        var after = new byte[1];
        Assert.True(m.Read(nation + 0x10, after));
        Assert.Equal(0b1011_0001, after[0]);
    }

    [Fact]
    public void A_choice_with_stored_values_maps_between_the_list_and_the_game_numbers()
    {
        var m = new FakeMemory().Map(Module, 0x1000, writable: false).Map(Heap, 0x1000);
        nint nation = Heap + 0x100;
        m.I64(Module + 0x800, Heap);
        m.I64(Heap, Heap + 0x80);
        m.I32(Heap + 8, 1);
        m.I64(Heap + 0x80, nation);
        m.I32(nation, 7);
        m.Bytes(nation + 0x10, 16);   // the game stores 16 for the third choice

        var def = new GameDefinition
        {
            Roots = { ["Nations"] = new RootDef { ModuleOffset = "0x800", Deref = ["0x0"] } },
            Collections = { ["Nation"] = new CollectionDef { Root = "Nations", Layout = CollectionLayout.PointerArray, CountOffset = "0x8" } },
            Structs =
            {
                ["Nation"] = new()
                {
                    ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 },
                    ["Continent"] = new FieldDef { Offset = "0x10", Type = FieldType.U8, Values = [0, 4, 16, 20] },
                },
            },
        };
        using var db = new LiveDatabase(m, def, Module, 0x1000, "test");
        var e = Assert.Single(db.GetAll(EntityKind.Nation));

        Assert.Equal(2, db.Read(e, "Continent"));

        db.Write(e, "Continent", 3);
        var after = new byte[1];
        Assert.True(m.Read(nation + 0x10, after));
        Assert.Equal(20, after[0]);

        // A number the list does not know reads as empty instead of as a wrong choice.
        m.Bytes(nation + 0x10, 9);
        Assert.Null(db.Read(e, "Continent"));
    }

    [Fact]
    public void Every_kind_has_a_name_and_generated_properties_are_typed()
    {
        foreach (var kind in Enum.GetValues<EntityKind>())
        {
            Assert.NotNull(PropertyCatalog.Find(kind, PropertyCatalog.DisplayName));
            Assert.Equal(PropertyCatalog.For(kind).Count, PropertyCatalog.For(kind).Select(p => p.Key).Distinct().Count());
        }

        var capacity = PropertyCatalog.Get(EntityKind.Stadium, "Capacity");
        Assert.Equal(ValueKind.Integer, capacity.Kind);

        var city = PropertyCatalog.Get(EntityKind.Stadium, "City");
        Assert.Equal(ValueKind.Reference, city.Kind);
        Assert.Equal(EntityKind.City, city.RefKind);

        // A team has no name of its own; it points at its club.
        Assert.Equal(EntityKind.Club, PropertyCatalog.Get(EntityKind.Team, "Club").RefKind);
    }
}

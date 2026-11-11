using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Tests;

/// <summary>
/// A transfer in live memory: the contract's team changes, every other copy of the link follows, and
/// the player moves between squad lists, borrowing a roomier list's storage when the new one is full.
/// </summary>
public class LiveTransferTests
{
    private const nint Module = 0x10000;
    private const nint Heap = 0x20000;

    private sealed record Fixture(FakeMemory Mem, LiveDatabase Db, nint P1, nint P2, nint TeamA, nint TeamB, nint TeamD, nint National, nint Contract1)
    {
        public Entity Player1 => Db.Find(EntityKind.Player, 1)!;
    }

    /// <summary>
    /// Two clubs. Club A has a first team (squad: both players, full) and a B team; club B has one team
    /// whose squad has one player and <paramref name="room"/> free slots. A third team, D, holds a squad list with
    /// <paramref name="donorCapacity"/> slots and one player, which a full list can borrow. Both players
    /// are contracted to A's first team and name it as their squad team; player 1 also names a national
    /// team in another slot.
    /// </summary>
    private static Fixture Build(int room, int donorCapacity = 0)
    {
        var m = new FakeMemory().Map(Module, 0x1000, writable: false).Map(Heap, 0x10000);
        m.I64(Module + 0x800, Heap);          // Players root: vector
        m.I64(Module + 0x900, Heap + 0x100);  // Clubs root: pointer array + count
        m.I64(Module + 0xA00, Heap + 0x200);  // Teams root: pointer array + count

        nint p1 = Heap + 0x1000, p2 = Heap + 0x1100;
        nint clubA = Heap + 0x2000, clubB = Heap + 0x2100;
        nint teamA = Heap + 0x3000, teamA2 = Heap + 0x3100, teamB = Heap + 0x3200, teamD = Heap + 0x3300, national = Heap + 0x3400;
        nint c1 = Heap + 0x4000, c2 = Heap + 0x4100;

        m.I64(Heap, Heap + 0x500); m.I64(Heap + 8, Heap + 0x510);
        m.I64(Heap + 0x500, p1); m.I64(Heap + 0x508, p2);
        m.I64(Heap + 0x100, Heap + 0x600); m.I32(Heap + 0x108, 2);
        m.I64(Heap + 0x600, clubA); m.I64(Heap + 0x608, clubB);
        m.I64(Heap + 0x200, Heap + 0x700); m.I32(Heap + 0x208, 4);
        m.I64(Heap + 0x700, teamA); m.I64(Heap + 0x708, teamA2); m.I64(Heap + 0x710, teamB); m.I64(Heap + 0x718, teamD);

        void Vector(nint at, nint storage, int capacity, params nint[] items)
        {
            for (var i = 0; i < items.Length; i++) m.I64(storage + i * 8, items[i]);
            m.I64(at, storage); m.I64(at + 8, storage + items.Length * 8); m.I64(at + 16, storage + capacity * 8);
        }

        m.I32(clubA, 10); Vector(clubA + 0x18, Heap + 0x5000, 2, teamA, teamA2);
        m.I32(clubB, 20); Vector(clubB + 0x18, Heap + 0x5100, 1, teamB);
        m.I32(teamA, 100); m.I64(teamA + 0x30, clubA); Vector(teamA + 0x38, Heap + 0x6000, 2, p1, p2);
        m.I32(teamA2, 101); m.I64(teamA2 + 0x30, clubA); Vector(teamA2 + 0x38, Heap + 0x6100, 0);
        m.I32(teamB, 200); m.I64(teamB + 0x30, clubB); Vector(teamB + 0x38, Heap + 0x6200, 1 + room, Heap + 0x7780);
        m.I32(teamD, 300);
        if (donorCapacity > 0) Vector(teamD + 0x38, Heap + 0x6300, donorCapacity, Heap + 0x7770);
        m.I32(p1, 1); m.I64(p1 + 0x50, c1); m.I64(c1 + 0x10, teamA); m.I64(c1 + 0x18, teamA);
        m.I64(p1 + 0x48, teamA); m.I64(p1 + 0x40, national);
        m.I32(p2, 2); m.I64(p2 + 0x50, c2); m.I64(c2 + 0x10, teamA); m.I64(c2 + 0x18, teamA); m.I64(p2 + 0x48, teamA);

        var def = new GameDefinition
        {
            Version = "test",
            Roots =
            {
                ["Players"] = new RootDef { ModuleOffset = "0x800", Deref = ["0x0"] },
                ["Clubs"] = new RootDef { ModuleOffset = "0x900", Deref = ["0x0"] },
                ["Teams"] = new RootDef { ModuleOffset = "0xA00", Deref = ["0x0"] },
            },
            Collections =
            {
                ["Player"] = new CollectionDef { Root = "Players" },
                ["Club"] = new CollectionDef { Root = "Clubs", Layout = CollectionLayout.PointerArray, CountOffset = "0x8" },
                ["Team"] = new CollectionDef { Root = "Teams", Layout = CollectionLayout.PointerArray, CountOffset = "0x8" },
            },
            Structs =
            {
                ["Player"] = new()
                {
                    ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 },
                    ["Contract.Club"] = new FieldDef
                    {
                        Offset = "0x30", Type = FieldType.EntityPtr, Path = ["0x50", "0x10"],
                        StoreAt = new FieldLocation { Path = ["0x50"], Offset = "0x10" },
                        StoreTarget = ["0x18", "0x0"],
                        AlsoStoreAt =
                        [
                            new FieldLocation { Path = ["0x50"], Offset = "0x18" },
                            new FieldLocation { Offset = "0x48" },
                            new FieldLocation { Offset = "0x40" },
                        ],
                        Members = new MemberListDef { List = "0x38", SiblingsPath = ["0x30"], Siblings = "0x18", Donors = "Team" },
                    },
                },
                ["Club"] = new() { ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 } },
                ["Team"] = new() { ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 } },
            },
        };
        return new Fixture(m, new LiveDatabase(m, def, Module, 0x1000, "fake"), p1, p2, teamA, teamB, teamD, national, c1);
    }

    private static List<nint> Squad(FakeMemory m, nint team)
    {
        var begin = m.ReadPointer(team + 0x38);
        var end = m.ReadPointer(team + 0x40);
        return Enumerable.Range(0, (int)((end - begin) / 8)).Select(i => m.ReadPointer(begin + i * 8)).ToList();
    }

    private static int Capacity(FakeMemory m, nint team) => (int)((m.ReadPointer(team + 0x48) - m.ReadPointer(team + 0x38)) / 8);

    [Fact]
    public void Moves_every_link_and_the_squad_entry_not_the_whole_team()
    {
        var f = Build(room: 3);
        var m = f.Mem;

        f.Db.Write(f.Player1, "Contract.Club", 20);

        Assert.Equal(f.TeamB, m.ReadPointer(f.Contract1 + 0x10));
        Assert.Equal(f.TeamB, m.ReadPointer(f.Contract1 + 0x18));
        Assert.Equal(f.TeamB, m.ReadPointer(f.P1 + 0x48));             // the squad team, which the game's profile shows
        Assert.Equal(f.National, m.ReadPointer(f.P1 + 0x40));          // not one of the old club's teams: left alone
        Assert.Equal(20, f.Db.Read(f.Player1, "Contract.Club"));
        Assert.Equal(10, f.Db.Read(f.Db.Find(EntityKind.Player, 2)!, "Contract.Club"));   // team-mate stays
        Assert.Equal(f.TeamA, m.ReadPointer(f.P2 + 0x48));
        Assert.Equal([f.P2], Squad(m, f.TeamA));
        Assert.Equal([Heap + 0x7780, f.P1], Squad(m, f.TeamB));
        Assert.Equal(Heap + 0x2000, m.ReadPointer(f.TeamA + 0x30));    // the team still belongs to A

        // Undo goes back the same way.
        f.Db.Write(f.Player1, "Contract.Club", 10);
        Assert.Equal(f.TeamA, m.ReadPointer(f.Contract1 + 0x10));
        Assert.Equal(f.TeamA, m.ReadPointer(f.P1 + 0x48));
        Assert.Equal([Heap + 0x7780], Squad(m, f.TeamB));
        Assert.Contains(f.P1, Squad(m, f.TeamA));
    }

    [Fact]
    public void A_full_squad_list_borrows_the_storage_of_a_roomier_one()
    {
        var f = Build(room: 0, donorCapacity: 4);
        var m = f.Mem;
        var given = m.ReadPointer(f.TeamB + 0x38);

        f.Db.Write(f.Player1, "Contract.Club", 20);

        Assert.Equal([Heap + 0x7780, f.P1], Squad(m, f.TeamB));
        Assert.Equal(4, Capacity(m, f.TeamB));
        Assert.Equal([Heap + 0x7770], Squad(m, f.TeamD));               // the lender keeps its players,
        Assert.Equal(given, m.ReadPointer(f.TeamD + 0x38));              // in the block the borrower gave up
    }

    [Fact]
    public void Nothing_is_written_when_the_new_squad_is_full_and_nothing_can_be_borrowed()
    {
        var f = Build(room: 0);
        var m = f.Mem;

        Assert.Throws<NotSupportedException>(() => f.Db.Write(f.Player1, "Contract.Club", 20));

        Assert.Equal(f.TeamA, m.ReadPointer(f.Contract1 + 0x10));
        Assert.Equal(f.TeamA, m.ReadPointer(f.P1 + 0x48));
        Assert.Equal([f.P1, f.P2], Squad(m, f.TeamA));
    }
}

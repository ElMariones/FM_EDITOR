using FMOpenEditor.Core.Data;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Editing;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.Tests;

/// <summary>
/// The parts of a live move beyond the links: undo that refuses once the game has changed what was
/// written, noticing a reload, release clauses, a human manager's selections, and signing a free agent
/// with a contract from the game's pool.
/// </summary>
public class LiveMoveTests
{
    private const nint Module = 0x10000;
    private const nint Heap = 0x20000;
    private const long ContractVt = 0x7777;
    private const long SelectionVt = 0x8888;

    private const nint P1 = Heap + 0x1000, P2 = Heap + 0x1100, P3 = Heap + 0x1200;
    private const nint ClubA = Heap + 0x2000, ClubB = Heap + 0x2100;
    private const nint TeamA = Heap + 0x3000, TeamB = Heap + 0x3200;
    private const nint Chunk = Heap + 0x9000, C1 = Chunk, C2 = Chunk + 200, FreeSlot = Chunk + 400;
    private const nint PoolHeader = Module + 0x400;
    private const nint Sel1 = Heap + 0xA000, Sel2 = Heap + 0xA200, Sel3 = Heap + 0xA400;
    private const nint Clauses = Heap + 0xB000;

    private sealed record Fixture(FakeMemory Mem, LiveDatabase Db)
    {
        public Entity Player(int id) => Db.Find(EntityKind.Player, id)!;
    }

    private static Fixture Build()
    {
        var m = new FakeMemory().Map(Module, 0x1000).Map(Heap, 0x10000);
        m.I64(Module + 0x800, Heap);          // Players root: vector
        m.I64(Module + 0x900, Heap + 0x100);  // Clubs root
        m.I64(Module + 0xA00, Heap + 0x200);  // Teams root

        m.I64(Heap, Heap + 0x500); m.I64(Heap + 8, Heap + 0x518);
        m.I64(Heap + 0x500, P1); m.I64(Heap + 0x508, P2); m.I64(Heap + 0x510, P3);
        m.I64(Heap + 0x100, Heap + 0x600); m.I32(Heap + 0x108, 2);
        m.I64(Heap + 0x600, ClubA); m.I64(Heap + 0x608, ClubB);
        m.I64(Heap + 0x200, Heap + 0x700); m.I32(Heap + 0x208, 2);
        m.I64(Heap + 0x700, TeamA); m.I64(Heap + 0x708, TeamB);

        void Vector(nint at, nint storage, int capacity, params nint[] items)
        {
            for (var i = 0; i < items.Length; i++) m.I64(storage + i * 8, items[i]);
            m.I64(at, storage); m.I64(at + 8, storage + items.Length * 8); m.I64(at + 16, storage + capacity * 8);
        }

        m.I32(ClubA, 10); Vector(ClubA + 0x18, Heap + 0x5000, 1, TeamA);
        m.I32(ClubB, 20); Vector(ClubB + 0x18, Heap + 0x5100, 1, TeamB);
        m.I32(TeamA, 100); m.I64(TeamA + 0x30, ClubA); Vector(TeamA + 0x38, Heap + 0x6000, 4, P1, P2);
        m.I32(TeamB, 200); m.I64(TeamB + 0x30, ClubB); Vector(TeamB + 0x38, Heap + 0x6200, 4, Heap + 0x7780);

        // Players: id, person id (+0x8), contract (+0x50), squad team (+0x48), ability (+0x60).
        void Person(nint p, int id, uint personId, nint contract, nint team, short ability)
        {
            m.I32(p, id); m.I32(p + 8, (int)personId); m.I64(p + 0x50, contract); m.I64(p + 0x48, team); m.I16(p + 0x60, ability);
        }
        Person(P1, 1, 1001, C1, TeamA, 120);
        Person(P2, 2, 1002, C2, TeamA, 150);
        Person(P3, 3, 1003, 0, 0, 140);   // a free agent

        void Contract(nint c, nint owner, nint team, byte status)
        {
            m.I64(c, ContractVt); m.I64(c + 8, owner); m.I64(c + 0x10, team); m.I64(c + 0x18, team);
            m.Bytes(c + 0x26, 1); m.Bytes(c + 0x54, status); m.Bytes(c + 0x5D, 7); m.Bytes(c + 0x57, 1);
            m.I32(c + 0x44, (2020 << 16) | 0x8001); m.I64(c + 0x98, Heap + 0xEEEE);
        }
        Contract(C1, P1, TeamA, 3);
        Contract(C2, P2, TeamA, 2);
        // C1's clauses: an appearance fee, a release clause of 1,000,000 and a wage rise.
        m.I32(Clauses, 500); m.I16(Clauses + 4, -1); m.I16(Clauses + 6, 32);
        m.I32(Clauses + 8, 1_000_000); m.I16(Clauses + 12, -1); m.I16(Clauses + 14, 0);
        m.I32(Clauses + 16, -1); m.I16(Clauses + 20, 25); m.I16(Clauses + 22, 4);
        m.I64(C1 + 0x68, Clauses); m.I64(C1 + 0x70, Clauses + 24); m.I64(C1 + 0x78, Clauses + 32);

        // The pool: one chunk of three 200-byte slots, the third free.
        m.I64(Heap + 0x8000, Chunk);
        m.I64(PoolHeader, Heap + 0x8000); m.I64(PoolHeader + 8, Heap + 0x8008); m.I64(PoolHeader + 16, Heap + 0x8008);
        m.I64(PoolHeader + 24, FreeSlot); m.I32(PoolHeader + 32, 1); m.I32(PoolHeader + 36, 2);
        m.I32(PoolHeader + 40, 200); m.I32(PoolHeader + 44, 3); m.I32(PoolHeader + 48, 16);

        // Selections: the club's (mostly club A), a national team's, and a copy with no list (a stack's).
        void Selection(nint s, bool soundList, params uint[] ids)
        {
            m.I64(s, SelectionVt);
            if (soundList) Vector(s + 8, s + 0x100, 1, Heap + 0x7000);
            for (var i = 0; i < 26; i++) m.I32(s + 0x28 + i * 4, i < ids.Length ? (int)ids[i] : -1);
        }
        Selection(Sel1, true, 1001, 1002);
        Selection(Sel2, true, 1001, 5000, 5001, 5002);
        Selection(Sel3, false, 1001, 1002);

        var contract = new FieldDef
        {
            Offset = "0x30", Type = FieldType.EntityPtr, Path = ["0x50", "0x10"],
            StoreAt = new FieldLocation { Path = ["0x50"], Offset = "0x10" },
            StoreTarget = ["0x18", "0x0"],
            AlsoStoreAt = [new FieldLocation { Path = ["0x50"], Offset = "0x18" }, new FieldLocation { Offset = "0x48" }],
            Members = new MemberListDef { List = "0x38", SiblingsPath = ["0x30"], Siblings = "0x18", Donors = "Team" },
        };
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
                    ["CurrentAbility"] = new FieldDef { Offset = "0x60", Type = FieldType.I16 },
                    ["Contract.Club"] = contract,
                    ["Contract.ReleaseClause"] = new FieldDef
                    {
                        Offset = "0x68", Type = FieldType.U32, Path = ["0x50"],
                        Entry = new ListEntryDef { Stride = 8, KeyOffset = "0x6", Key = 0, NoValue = 0xFFFFFFFF },
                    },
                },
                ["Club"] = new() { ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 } },
                ["Team"] = new() { ["Id"] = new FieldDef { Offset = "0x0", Type = FieldType.I32 } },
            },
            Selections = new SelectionDef { Class = "sel", PersonId = "0x8" },
            ContractPool = new ContractPoolDef
            {
                Class = "contract", Size = 200, Owner = "0x8", Teams = ["0x10", "0x18"], Job = "0x26",
                SquadStatus = "0x54", KeeperStatuses = [15, 16, 17, 20, 21], Dates = ["0x44", "0x4C"],
                SquadNumber = "0x5D", Listing = "0x57", Cleared = ["0x68", "0x70", "0x78", "0x98"],
            },
        };
        var db = new LiveDatabase(m, def, Module, 0x1000, "fake");
        db.UseMoveClasses((nint)SelectionVt, [Sel1, Sel2, Sel3], (nint)ContractVt);
        return new Fixture(m, db);
    }

    private static uint Slot(FakeMemory m, nint sel, int i) => m.ReadUInt32(sel + 0x28 + i * 4);

    [Fact]
    public void A_move_empties_the_players_place_in_his_clubs_selections_only()
    {
        var f = Build();
        f.Db.Write(f.Player(1), "Contract.Club", 20);

        Assert.Equal(uint.MaxValue, Slot(f.Mem, Sel1, 0));   // his club's: emptied
        Assert.Equal(1002u, Slot(f.Mem, Sel1, 1));           // team-mate stays
        Assert.Equal(1001u, Slot(f.Mem, Sel2, 0));           // a national team's: left alone
        Assert.Equal(1001u, Slot(f.Mem, Sel3, 0));           // no sound list: left alone
    }

    [Fact]
    public void Undo_puts_back_exactly_what_the_save_wrote()
    {
        var f = Build();
        using var s = new EditorSession(f.Db);
        s.Set(f.Player(1), "Contract.Club", 20);
        s.Save();
        Assert.Equal(TeamB, f.Mem.ReadPointer(C1 + 0x10));

        s.UndoLastSave();

        Assert.Equal(TeamA, f.Mem.ReadPointer(C1 + 0x10));
        Assert.Equal(TeamA, f.Mem.ReadPointer(P1 + 0x48));
        Assert.Equal(1001u, Slot(f.Mem, Sel1, 0));
        Assert.Equal(P1, f.Mem.ReadPointer(Heap + 0x6000));
        Assert.Equal(Heap + 0x6010, f.Mem.ReadPointer(TeamA + 0x40));
        Assert.Equal(Heap + 0x6208, f.Mem.ReadPointer(TeamB + 0x40));
    }

    [Fact]
    public void Undo_is_refused_once_the_game_has_changed_what_was_written()
    {
        var f = Build();
        using var s = new EditorSession(f.Db);
        s.Set(f.Player(1), "Contract.Club", 20);
        s.Save();
        f.Mem.I32(Sel1 + 0x28, 1002);                        // the manager picked someone else meanwhile

        Assert.Throws<UndoRefusedException>(() => s.UndoLastSave());

        Assert.Equal(TeamB, f.Mem.ReadPointer(C1 + 0x10));   // nothing was put back
        Assert.False(s.Changes.CanUndo);
    }

    [Fact]
    public void Writes_stop_once_the_game_has_loaded_another_save()
    {
        var f = Build();
        Assert.False(f.Db.CheckStale());
        foreach (var a in new[] { P1, P2, P3, ClubA, ClubB }) f.Mem.I64(a, 0x5A5A5A5A);

        Assert.True(f.Db.CheckStale());
        Assert.Throws<StaleDataException>(() => f.Db.Write(f.Player(2), "Contract.Club", 20));
        Assert.Equal(TeamA, f.Mem.ReadPointer(C2 + 0x10));
    }

    [Fact]
    public void The_release_clause_is_the_kind_0_clause_and_can_be_changed_or_removed()
    {
        var f = Build();
        var p1 = f.Player(1);
        Assert.Equal(1_000_000L, f.Db.Read(p1, "Contract.ReleaseClause"));
        Assert.Null(f.Db.Read(f.Player(2), "Contract.ReleaseClause"));

        f.Db.Write(p1, "Contract.ReleaseClause", 2_500_000L);
        Assert.Equal(2_500_000L, f.Db.Read(p1, "Contract.ReleaseClause"));
        Assert.Equal(500, f.Mem.ReadInt32(Clauses));         // the clause before it is untouched

        f.Db.Write(p1, "Contract.ReleaseClause", 0L);
        Assert.Null(f.Db.Read(p1, "Contract.ReleaseClause"));
        Assert.Equal(Clauses + 16, f.Mem.ReadPointer(C1 + 0x70));
        Assert.Equal(4, f.Mem.ReadUInt16(Clauses + 14));     // the wage rise moved up

        Assert.Throws<NotSupportedException>(() => f.Db.Write(f.Player(2), "Contract.ReleaseClause", 5L));
    }

    [Fact]
    public void A_free_agent_signs_with_a_contract_from_the_games_pool()
    {
        var f = Build();
        var p3 = f.Player(3);
        Assert.Null(f.Db.WriteProblem(p3, "Contract.Club", 10));

        f.Db.Write(p3, "Contract.Club", 10);

        Assert.Equal(FreeSlot, f.Mem.ReadPointer(P3 + 0x50));
        Assert.Equal(ContractVt, f.Mem.ReadInt64(FreeSlot));
        Assert.Equal(P3, f.Mem.ReadPointer(FreeSlot + 8));
        Assert.Equal(TeamA, f.Mem.ReadPointer(FreeSlot + 0x10));
        Assert.Equal(TeamA, f.Mem.ReadPointer(FreeSlot + 0x18));
        Assert.Equal(TeamA, f.Mem.ReadPointer(P3 + 0x48));
        Assert.Equal(2, f.Mem.ReadByte(FreeSlot + 0x54));           // modelled on P2, nearest in ability
        Assert.Equal(255, f.Mem.ReadByte(FreeSlot + 0x5D));
        Assert.Equal(0, f.Mem.ReadByte(FreeSlot + 0x57));
        Assert.Equal(0, f.Mem.ReadInt64(FreeSlot + 0x98));          // nothing shared with the template
        Assert.Equal(0x8000, f.Mem.ReadUInt16(FreeSlot + 0x44) & 0xFE00);   // the date keeps its flags
        Assert.Contains(P3, Enumerable.Range(0, 3).Select(i => f.Mem.ReadPointer(Heap + 0x6000 + i * 8)));
        Assert.Equal(10, f.Db.Read(p3, "Contract.Club"));
        Assert.Equal(0, f.Mem.ReadInt64(PoolHeader + 24));           // no free slot left
        Assert.Equal(0u, f.Mem.ReadUInt32(PoolHeader + 32));
        Assert.Equal(3u, f.Mem.ReadUInt32(PoolHeader + 36));
    }

    [Fact]
    public void Signing_is_refused_up_front_when_the_pool_has_no_free_slot()
    {
        var f = Build();
        f.Mem.I64(PoolHeader + 24, 0); f.Mem.I32(PoolHeader + 32, 0); f.Mem.I32(PoolHeader + 44, 2);

        Assert.Contains("no spare contract", f.Db.WriteProblem(f.Player(3), "Contract.Club", 10));
        Assert.Throws<NotSupportedException>(() => f.Db.Write(f.Player(3), "Contract.Club", 10));
        Assert.Equal(0, f.Mem.ReadInt64(P3 + 0x50));
    }

    [Fact]
    public void Undoing_a_signing_gives_the_slot_back_to_the_pool()
    {
        var f = Build();
        using var s = new EditorSession(f.Db);
        s.Set(f.Player(3), "Contract.Club", 10);
        Assert.Empty(s.Save());
        Assert.Equal(FreeSlot, f.Mem.ReadPointer(P3 + 0x50));
        Assert.Equal(1, s.UndoLastSave());

        Assert.Equal(0, f.Mem.ReadInt64(P3 + 0x50));
        Assert.Equal(FreeSlot, f.Mem.ReadPointer(PoolHeader + 24));
        Assert.Equal(1u, f.Mem.ReadUInt32(PoolHeader + 32));
        Assert.DoesNotContain(P3, Enumerable.Range(0, 2).Select(i => f.Mem.ReadPointer(Heap + 0x6000 + i * 8)));
    }
}

using System.Buffers.Binary;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Research;

namespace FMOpenEditor.Core.Data;

/// <summary>
/// The parts of a move beyond the links themselves: a human manager's team selections, and a contract
/// for a free agent taken from the game's own pool.
/// </summary>
public sealed partial class LiveDatabase
{
    private nint _selectionVtable;
    private List<nint> _selections = [];
    private nint _contractVtable;

    private static nint Off(string text) => (nint)GameDefinition.ParseOffset(text);

    // ---- team selections ----

    /// <summary>
    /// The selection slots that hold <paramref name="person"/> in copies of his old club's selections:
    /// those whose filled slots are mostly players of <paramref name="oldTeams"/>. A national team's
    /// selection, full of other clubs' players, is left alone, and so is any copy without a sound list
    /// (a thread's stack, or a block the game has let go of).
    /// </summary>
    private List<nint> PlanSelectionClears(nint person, IEnumerable<nint> oldTeams, string squadList)
    {
        var plan = new List<nint>();
        if (_def.Selections is not { } sel || _selections.Count == 0) return plan;
        var personId = Off(sel.PersonId);
        var offset = _objectOffset.GetValueOrDefault(person);
        var id = _memory.ReadUInt32(person + personId);

        var clubIds = new HashSet<uint>();
        foreach (var team in oldTeams)
            foreach (var obj in ReadPointerList(team + Off(squadList)))
                if (TryReadU32(obj + offset + personId) is { } pid) clubIds.Add(pid);
        if (clubIds.Count == 0) return plan;

        var slotsAt = Off(sel.Slots);
        foreach (var s in _selections)
        {
            if (!TryReadPointer(s, out var vt) || vt != _selectionVtable) continue;
            if (!SoundList(s + Off(sel.List))) continue;
            byte[] raw;
            try { raw = _memory.ReadBytes(s + slotsAt, sel.Count * 4); }
            catch (MemoryAccessException) { continue; }
            var slots = Enumerable.Range(0, sel.Count).Select(i => BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4))).ToArray();
            var mine = Array.IndexOf(slots, id);
            if (mine < 0) continue;
            var filled = slots.Where(x => x != uint.MaxValue).ToList();
            if (filled.Count(clubIds.Contains) * 2 <= filled.Count) continue;
            for (var i = 0; i < slots.Length; i++)
                if (slots[i] == id) plan.Add(s + slotsAt + i * 4);
        }
        return plan;
    }

    private bool SoundList(nint vector)
    {
        if (!TryReadPointer(vector, out var b) || !TryReadPointer(vector + 8, out var e) || !TryReadPointer(vector + 16, out var c))
            return false;
        return b != 0 && e >= b && c >= e && c - b <= 0x10000 && (e - b) % 8 == 0;
    }

    private bool TryReadPointer(nint address, out nint value)
    {
        var buf = new byte[8];
        var ok = _memory.Read(address, buf);
        value = ok ? (nint)BinaryPrimitives.ReadInt64LittleEndian(buf) : 0;
        return ok;
    }

    private uint? TryReadU32(nint address)
    {
        var buf = new byte[4];
        return _memory.Read(address, buf) ? BinaryPrimitives.ReadUInt32LittleEndian(buf) : null;
    }

    /// <summary>How many selection objects the last refresh found (for diagnostics and tests).</summary>
    public int SelectionCount => _selections.Count;

    // ---- the contract pool ----

    private readonly record struct Pool(nint Header, nint FirstFree, uint Free, uint Used, uint SlotSize);

    /// <summary>
    /// Finds the contract pool in the module's writable data: a list of chunk pointers, the first free
    /// slot, the counts and the slot size, and objects of the contract class in its first chunk. Read
    /// fresh every time, as the game changes it whenever it makes or lets go of a contract.
    /// </summary>
    private Pool? FindContractPool()
    {
        if (_def.ContractPool is not { } def || _contractVtable == 0) return null;
        if (_poolHeader != 0)
        {
            try
            {
                if (ParsePool(_memory.ReadBytes(_poolHeader, 56), 0, _poolHeader, def) is { } known) return known;
            }
            catch (MemoryAccessException) { }
            _poolHeader = 0;
        }
        var end = _moduleBase + (nint)_moduleSize;
        foreach (var region in _memory.Regions())
        {
            if (!region.Writable || region.Start < _moduleBase || region.Start >= end) continue;
            var size = (int)Math.Min(region.Size, 64 << 20);
            byte[] data;
            try { data = _memory.ReadBytes(region.Start, size); }
            catch (MemoryAccessException) { continue; }
            for (var i = 0; i + 56 <= data.Length; i += 8)
                if (ParsePool(data, i, region.Start + i, def) is { } found)
                {
                    _poolHeader = found.Header;
                    return found;
                }
        }
        return null;
    }

    private nint _poolHeader;

    private Pool? ParsePool(byte[] data, int i, nint at, ContractPoolDef def)
    {
        var b = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i));
        var e = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + 8));
        var c = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + 16));
        if (b < 0x10000 || e <= b || c < e || (e - b) % 8 != 0 || (e - b) / 8 > 4096) return null;
        var slotSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 40));
        if (slotSize != def.Size) return null;
        var ff = (nint)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(i + 24));
        var free = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 32));
        var used = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 36));
        var first = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 44));
        var later = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i + 48));
        var chunks = (e - b) / 8;
        if (first == 0 || free + used != first + later * (chunks - 1) || (ff == 0) != (free == 0)) return null;
        if (!TryReadPointer((nint)b, out var chunk0)) return null;
        for (var k = 0; k < 4; k++)
            if (TryReadPointer(chunk0 + k * (nint)slotSize, out var vt) && vt == _contractVtable)
                return new Pool(at, ff, free, used, slotSize);
        return null;
    }

    /// <summary>Why a free agent can't be signed right now, or null when he can.</summary>
    private string? SigningProblem()
    {
        if (_def.ContractPool is null) return "Signing free agents isn't supported for this game version yet.";
        if (FindContractPool() is not { } pool) return "The game's list of contracts couldn't be found, so free agents can't be signed.";
        if (pool.Free == 0)
            return "The game has no spare contract to give a free agent yet. It makes them as it plays on: continue a day or two in the game and try again.";
        return null;
    }

    /// <summary>
    /// Works out a new contract for <paramref name="person"/> with <paramref name="team"/>: a team-mate's
    /// contract copied into the pool's first free slot and made his. Returns the writes, to be made
    /// after everything else has been checked.
    /// </summary>
    private (nint Slot, byte[] Bytes, nint Header, byte[] HeaderBytes) PlanSigning(nint person, nint team, string squadList, string contractAt)
    {
        var def = _def.ContractPool!;
        if (SigningProblem() is { } problem) throw new NotSupportedException(problem);
        var pool = FindContractPool()!.Value;
        var slot = pool.FirstFree;
        var nextFree = _memory.ReadPointer(slot);

        var template = PickTemplate(person, team, squadList, contractAt)
                       ?? throw new NotSupportedException("The new club's first team has no player contract to model the new one on.");
        var bytes = _memory.ReadBytes(template, def.Size);
        void Q(string at, long v) => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan((int)Off(at)), v);

        Q(def.Owner, person);
        foreach (var t in def.Teams) Q(t, team);
        bytes[Off(def.Job)] = (byte)def.PlayerJob;
        var date = GameDate;
        foreach (var at in def.Dates)
        {
            var o = (int)Off(at);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(o)) & ~0x1FF;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o), (ushort)(flags | (date.DayOfYear - 1 + _def.DateDayBase)));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(o + 2), (ushort)date.Year);
        }
        if (def.SquadNumber is { } sn) bytes[Off(sn)] = 255;
        if (def.Listing is { } l) bytes[Off(l)] = 0;
        foreach (var at in def.Cleared) Q(at, 0);

        var header = new byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(header, nextFree);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), pool.Free - 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), pool.Used + 1);
        return (slot, bytes, pool.Header + 24, header);
    }

    /// <summary>
    /// The contract of a player in <paramref name="team"/>'s squad to copy: one whose squad status fits
    /// (a keeper's for a keeper), nearest in current ability.
    /// </summary>
    private nint? PickTemplate(nint person, nint team, string squadList, string contractAt)
    {
        var def = _def.ContractPool!;
        var offset = _objectOffset.GetValueOrDefault(person);
        var player = FieldsFor(EntityKind.Player);
        int Ability(nint p) => player?.GetValueOrDefault("CurrentAbility") is { IsMapped: true } f ? (int)ReadRaw(p, f) : 0;
        bool Keeper(nint p) => player?.GetValueOrDefault("Pos.GK") is { IsMapped: true } f && ReadRaw(p, f) >= 15;
        var mine = Ability(person);
        var keeper = Keeper(person);

        (nint Contract, int Score)? best = null;
        foreach (var obj in ReadPointerList(team + Off(squadList)))
        {
            var p = obj + offset;
            if (!TryReadPointer(p + Off(contractAt), out var c) || c == 0) continue;
            if (!TryReadPointer(c + Off(def.Owner), out var owner) || owner != p) continue;
            if (!TryReadPointer(c + Off(def.Teams[0]), out var t) || t != team) continue;
            var status = TryReadU32(c + Off(def.SquadStatus)) is { } s ? s & 0xFF : 0;
            var fits = def.KeeperStatuses.Contains(status) == keeper;
            var score = Math.Abs(Ability(p) - mine) + (fits ? 0 : 1000);
            if (best is null || score < best.Value.Score) best = (c, score);
        }
        return best?.Contract;
    }

    /// <summary>Called once the RTTI classes are known: the vtables a move needs.</summary>
    private void NoteMoveClasses(IReadOnlyDictionary<string, RttiClass> byName)
    {
        static nint Primary(RttiClass c) => c.Vtables.FirstOrDefault(v => v.ObjectOffset == 0)?.Address ?? 0;
        _selectionVtable = _def.Selections is { } s && byName.TryGetValue(s.Class, out var sc) ? Primary(sc) : 0;
        _contractVtable = _def.ContractPool is { } c && byName.TryGetValue(c.Class, out var cc) ? Primary(cc) : 0;
    }

    public string? WriteProblem(Entity entity, string key, object? value)
    {
        if (IsStale) return new StaleDataException().Message;
        if (Field(entity.Kind, key) is not { StoreAt: { } storeAt } || value is null) return null;
        try
        {
            return Locate(entity.Address, storeAt) is null ? SigningProblem() : null;
        }
        catch (MemoryAccessException ex) { return ex.Message; }
    }

    /// <summary>For tests on fake memory, which has no RTTI: the classes a move needs and the selections.</summary>
    internal void UseMoveClasses(nint selectionVtable, IEnumerable<nint> selections, nint contractVtable)
    {
        _selectionVtable = selectionVtable;
        _selections = selections.ToList();
        _contractVtable = contractVtable;
    }
}

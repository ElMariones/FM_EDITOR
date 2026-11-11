using System.Buffers.Binary;
using System.Text;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;
using FMOpenEditor.Core.Research;

namespace FMOpenEditor.Core.Data;

/// <summary>Reads and writes a running game's memory, driven entirely by a <see cref="GameDefinition"/>.</summary>
public sealed class LiveDatabase : IGameDatabase
{
    private readonly IMemoryAccess _memory;
    private readonly GameDefinition _def;
    private readonly nint _moduleBase;
    private readonly long _moduleSize;
    private readonly IDisposable? _owner;

    private readonly Dictionary<string, nint> _roots = [];
    private readonly Dictionary<EntityKind, List<Entity>> _entities = [];
    private readonly Dictionary<EntityKind, Dictionary<int, Entity>> _byId = [];
    private readonly Dictionary<nint, Entity> _byAddress = [];
    // Where each RTTI-found record's object starts: the record is the vtable slot of one base class,
    // and squad lists hold the start of the whole object.
    private readonly Dictionary<nint, int> _objectOffset = [];
    private List<RttiClass>? _rttiClasses;

    public string DisplayName { get; }
    public bool IsLive => true;
    public GameDefinition Definition => _def;

    /// <summary>Problems found while resolving roots and collections; non-empty means partial support.</summary>
    public List<string> Warnings { get; } = [];

    public LiveDatabase(IMemoryAccess memory, GameDefinition definition, nint moduleBase, long moduleSize,
        string displayName, IDisposable? owner = null)
    {
        _memory = memory;
        _def = definition;
        _moduleBase = moduleBase;
        _moduleSize = moduleSize;
        _owner = owner;
        DisplayName = displayName;
        ResolveRoots();
        Refresh();
    }

    public DateOnly GameDate
    {
        get
        {
            if (_def.GameDate is { IsMapped: true } f && _def.GameDateRoot is { } r && _roots.TryGetValue(r, out var root))
            {
                if (ReadField(root, f, ValueKind.Date) is DateOnly d) return d;
            }
            return DateOnly.FromDateTime(DateTime.Today);
        }
    }

    private void ResolveRoots()
    {
        foreach (var (name, root) in _def.Roots)
        {
            try
            {
                nint addr;
                if (!string.IsNullOrWhiteSpace(root.Pattern))
                {
                    var match = SignatureScanner.Find(_memory, _moduleBase, _moduleSize, root.Pattern)
                                ?? throw new InvalidOperationException("signature not found");
                    addr = SignatureScanner.ResolveRipRelative(_memory, match, root.DispOffset, root.InstructionLength);
                }
                else if (!string.IsNullOrWhiteSpace(root.ModuleOffset))
                {
                    addr = _moduleBase + (nint)GameDefinition.ParseOffset(root.ModuleOffset);
                }
                else
                {
                    Warnings.Add($"Root '{name}' is not mapped yet.");
                    continue;
                }

                foreach (var hop in root.Deref) addr = _memory.ReadPointer(addr + (nint)GameDefinition.ParseOffset(hop));
                _roots[name] = addr;
            }
            catch (Exception ex)
            {
                Warnings.Add($"Root '{name}': {ex.Message}");
            }
        }
    }

    public void Refresh()
    {
        _entities.Clear();
        _byId.Clear();
        _byAddress.Clear();
        _objectOffset.Clear();
        var rttiRecords = ScanRttiCollections();
        foreach (var kind in Enum.GetValues<EntityKind>())
        {
            var list = new List<Entity>();
            var ids = new Dictionary<int, Entity>();
            if (_def.Collections.TryGetValue(kind.ToString(), out var col))
            {
                IEnumerable<nint>? records = null;
                if (col.Layout == CollectionLayout.RttiInstances) records = rttiRecords.GetValueOrDefault(kind.ToString());
                else if (_roots.TryGetValue(col.Root, out var root)) records = EnumerateRecords(root, col);
                if (records is not null)
                try
                {
                    var idField = FieldsFor(kind)?.GetValueOrDefault("Id");
                    var index = 0;
                    foreach (var addr in records)
                    {
                        if (col.Filter is { IsMapped: true } filter && ReadRaw(addr, filter) != col.FilterValue) continue;
                        var id = idField is { IsMapped: true } ? (int)ReadRaw(addr, idField) : index;
                        index++;
                        var e = new Entity(kind, id, addr);
                        list.Add(e);
                        ids.TryAdd(id, e);
                        _byAddress.TryAdd(addr, e);
                    }
                }
                catch (Exception ex)
                {
                    Warnings.Add($"{kind} list: {ex.Message}");
                }
            }
            _entities[kind] = list;
            _byId[kind] = ids;
        }
    }

    /// <summary>
    /// Resolves every <see cref="CollectionLayout.RttiInstances"/> collection with one RTTI lookup and one
    /// pass over the heap. Keyed by entity kind name; each list is in address order without duplicates.
    /// </summary>
    private Dictionary<string, List<nint>> ScanRttiCollections()
    {
        var result = new Dictionary<string, List<nint>>();
        var wanted = _def.Collections.Where(c => c.Value.Layout == CollectionLayout.RttiInstances).ToList();
        if (wanted.Count == 0) return result;

        try
        {
            _rttiClasses ??= RttiScanner.FindClasses(_memory, _moduleBase, _moduleSize);
        }
        catch (Exception ex)
        {
            Warnings.Add($"RTTI: {ex.Message}");
            return result;
        }
        var byName = _rttiClasses.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());

        var vtablesByKind = new Dictionary<string, List<nint>>();
        var offsetOf = new Dictionary<nint, int>();
        foreach (var (kind, col) in wanted)
        {
            var list = vtablesByKind[kind] = [];
            foreach (var src in col.Classes ?? [])
            {
                if (!byName.TryGetValue(src.Class, out var cls)) { Warnings.Add($"{kind} list: class {src.Class} not found."); continue; }
                var offset = (int)GameDefinition.ParseOffset(src.VtableOffset);
                var vt = cls.Vtables.FirstOrDefault(v => v.ObjectOffset == offset);
                if (vt is null) { Warnings.Add($"{kind} list: {src.Class} has no vtable at offset 0x{offset:X}."); continue; }
                list.Add(vt.Address);
                offsetOf[vt.Address] = offset;
            }
        }

        var all = vtablesByKind.Values.SelectMany(v => v).Distinct().Select(v => new RttiVtable(v, 0, 0)).ToList();
        var max = wanted.Max(c => c.Value.MaxCount);
        var scan = RttiScanner.FindInstances(_memory, all, _moduleBase, _moduleSize, max);
        foreach (var (kind, vtables) in vtablesByKind)
        {
            foreach (var v in vtables)
                foreach (var r in scan.Samples.GetValueOrDefault(v) ?? []) _objectOffset.TryAdd(r, offsetOf[v]);
            var records = vtables.SelectMany(v => scan.Samples.GetValueOrDefault(v) ?? []).Distinct().Order().ToList();
            var cap = _def.Collections[kind].MaxCount;
            if (records.Count > cap) { Warnings.Add($"{kind} list: {records.Count:N0} objects, more than the {cap:N0} allowed."); records = records[..cap]; }
            result[kind] = records;
        }
        return result;
    }

    private IEnumerable<nint> EnumerateRecords(nint root, CollectionDef col)
    {
        switch (col.Layout)
        {
            case CollectionLayout.PointerRange:
            {
                var begin = _memory.ReadPointer(root + (nint)GameDefinition.ParseOffset(col.BeginOffset));
                var end = _memory.ReadPointer(root + (nint)GameDefinition.ParseOffset(col.EndOffset));
                var count = (end - begin) / 8;
                if (count < 0 || count > col.MaxCount) throw new InvalidDataException($"implausible count {count}");
                for (var i = 0; i < count; i++)
                {
                    var p = _memory.ReadPointer(begin + i * 8);
                    if (p != 0) yield return p;
                }
                break;
            }
            case CollectionLayout.PointerArray:
            case CollectionLayout.InlineArray:
            {
                var begin = _memory.ReadPointer(root + (nint)GameDefinition.ParseOffset(col.BeginOffset));
                var count = _memory.ReadInt32(root + (nint)GameDefinition.ParseOffset(col.CountOffset ?? "0x8"));
                if (count < 0 || count > col.MaxCount) throw new InvalidDataException($"implausible count {count}");
                for (var i = 0; i < count; i++)
                {
                    if (col.Layout == CollectionLayout.InlineArray)
                    {
                        yield return begin + i * col.Stride;
                        continue;
                    }
                    var p = _memory.ReadPointer(begin + i * 8);
                    if (p != 0) yield return p;
                }
                break;
            }
        }
    }

    public IReadOnlyList<Entity> GetAll(EntityKind kind) => _entities.GetValueOrDefault(kind) ?? [];

    public Entity? Find(EntityKind kind, int id) =>
        _byId.TryGetValue(kind, out var map) && map.TryGetValue(id, out var e) ? e : null;

    private Dictionary<string, FieldDef>? FieldsFor(EntityKind kind) => _def.Structs.GetValueOrDefault(kind.ToString());

    private FieldDef? Field(EntityKind kind, string key) =>
        FieldsFor(kind)?.GetValueOrDefault(key) is { IsMapped: true } f ? f : null;

    public bool Supports(EntityKind kind, string key) => Field(kind, key) is not null;

    public bool CanWrite(EntityKind kind, string key) => Field(kind, key) is { ReadOnly: false };

    public object? Read(Entity entity, string key)
    {
        var field = Field(entity.Kind, key) ?? throw new NotSupportedException($"{entity.Kind}.{key} is not mapped for {_def.Version}.");
        return ReadField(entity.Address, field, PropertyCatalog.Get(entity.Kind, key).Kind);
    }

    public void Write(Entity entity, string key, object? value)
    {
        var field = Field(entity.Kind, key) ?? throw new NotSupportedException($"{entity.Kind}.{key} is not mapped for {_def.Version}.");
        var prop = PropertyCatalog.Get(entity.Kind, key);
        if (field.ReadOnly) throw new NotSupportedException($"{prop.Label} can be read but not changed in this version.");
        var addr = FieldAddress(entity.Address, field)
                   ?? throw new NotSupportedException($"{prop.Label} is empty for this record, so there is nothing to edit in place.");

        switch (field.Type)
        {
            case FieldType.FmDate:
                var d = value as DateOnly? ?? default;
                var dateBytes = new byte[4];
                var dayBase = _def.DateDayBase;
                var emptyYear = dayBase == 1 ? 1900 : 0;
                BinaryPrimitives.WriteUInt16LittleEndian(dateBytes, (ushort)(value is null ? dayBase : d.DayOfYear - 1 + dayBase));
                BinaryPrimitives.WriteUInt16LittleEndian(dateBytes.AsSpan(2), (ushort)(value is null ? emptyYear : d.Year));
                _memory.WriteChecked(addr, dateBytes);
                break;
            case FieldType.Utf16Inline:
                var text = ((string?)value ?? "")[..Math.Min(((string?)value ?? "").Length, field.MaxLength - 1)];
                var textBytes = new byte[field.MaxLength * 2];
                Encoding.Unicode.GetBytes(text, textBytes);
                _memory.WriteChecked(addr, textBytes);
                break;
            case FieldType.Utf16Ptr or FieldType.Utf8Ptr:
                // Growing a heap string means allocating in the game; overwrite in place when it fits.
                var target = _memory.ReadPointer(addr);
                var enc = field.Type == FieldType.Utf16Ptr ? Encoding.Unicode : Encoding.UTF8;
                var current = _memory.ReadString(target, enc, field.MaxLength);
                var next = (string?)value ?? "";
                if (next.Length > current.Length)
                    throw new NotSupportedException($"{prop.Label} can be at most {current.Length} characters in a live game.");
                var bytes = enc.GetBytes(next + "\0");
                _memory.WriteChecked(target, bytes);
                break;
            case FieldType.FmString:
            {
                // Same rule as other heap strings: rewrite in place, never longer than what is there.
                var str = _memory.ReadPointer(addr);
                if (str == 0) throw new NotSupportedException($"{prop.Label} is empty for this record, so there is nothing to edit in place.");
                var currentBytes = _memory.ReadInt32(str);
                var newBytes = Encoding.UTF8.GetBytes((string?)value ?? "");
                if (currentBytes < 0 || newBytes.Length > currentBytes)
                    throw new NotSupportedException($"{prop.Label} can be at most {currentBytes} bytes (UTF-8) in a live game.");
                var block = new byte[4 + currentBytes + 1];
                BinaryPrimitives.WriteInt32LittleEndian(block, newBytes.Length);
                newBytes.CopyTo(block, 4);
                _memory.WriteChecked(str, block);
                break;
            }
            case FieldType.EntityPtr when field.StoreAt is not null:
                WriteStoredLink(entity, field, prop, value);
                break;
            case FieldType.EntityPtr:
                var refKind = prop.RefKind ?? throw new InvalidDataException($"{key} has no reference kind.");
                var ptr = value is int id && Find(refKind, id) is { } refEntity ? refEntity.Address : 0;
                _memory.WriteChecked(addr, BitConverter.GetBytes((long)ptr));
                break;
            case FieldType.Bool when !string.IsNullOrWhiteSpace(field.Mask):
            {
                // One flag in a shared byte: change that bit and leave its neighbours alone.
                var mask = (byte)GameDefinition.ParseOffset(field.Mask);
                var flags = _memory.ReadByte(addr);
                var on = value is not null && Values.ToDouble(value) != 0;
                _memory.WriteChecked(addr, [(byte)(on ? flags | mask : flags & ~mask)]);
                break;
            }
            case var _ when field.Values is { Count: > 0 } stored:
            {
                var index = value is null ? -1 : (int)Values.ToDouble(value);
                if (index < 0 || index >= stored.Count) throw new NotSupportedException($"{prop.Label} has no choice number {index}.");
                WriteNumber(addr, field, stored[index]);
                break;
            }
            default:
                WriteNumber(addr, field, value is null ? 0 : Values.ToDouble(value) * field.Scale);
                break;
        }
    }

    /// <summary>
    /// Writes a link that is read through a chain one link earlier (see <see cref="FieldDef.StoreAt"/>):
    /// a transfer. Everything is checked before the first byte is written, so a move that can't be
    /// made completely is refused rather than left half done.
    /// </summary>
    private void WriteStoredLink(Entity entity, FieldDef field, PropertyDef prop, object? value)
    {
        var refKind = prop.RefKind ?? throw new InvalidDataException($"{prop.Key} has no reference kind.");
        if (value is not int id || Find(refKind, id) is not { } target)
            throw new NotSupportedException($"{prop.Label} can't be emptied in a live game; release the player on a free instead.");

        var store = Locate(entity.Address, field.StoreAt!)
                    ?? throw new NotSupportedException($"{prop.Label} is empty for this record, so there is nothing to edit in place.");
        var next = Follow(target.Address, field.StoreTarget)
                   ?? throw new NotSupportedException($"{prop.Label}: that {refKind.ToString().ToLowerInvariant()} has nothing to link to here.");
        var previous = _memory.ReadPointer(store);

        // The old owner and its siblings (a club's other teams): links into this group move with the record.
        var members = field.Members;
        var oldGroup = new HashSet<nint>();
        if (previous != 0)
        {
            oldGroup.Add(previous);
            if (members?.Siblings is { } siblings && Follow(previous, members.SiblingsPath) is { } holder)
                oldGroup.UnionWith(ReadPointerList(holder + (nint)GameDefinition.ParseOffset(siblings)).Where(x => x != 0));
        }
        oldGroup.Remove(next);

        var member = entity.Address - _objectOffset.GetValueOrDefault(entity.Address);
        var list = members is null ? 0 : (nint)GameDefinition.ParseOffset(members.List);
        var add = members is null ? null : PlanAdd(next + list, member, members, oldGroup.Select(o => o + list).ToHashSet(), prop.Label);

        _memory.WriteChecked(store, BitConverter.GetBytes((long)next));
        foreach (var also in field.AlsoStoreAt ?? [])
        {
            if (Locate(entity.Address, also) is not { } at) continue;
            var current = _memory.ReadPointer(at);
            if (current == previous || oldGroup.Contains(current)) _memory.WriteChecked(at, BitConverter.GetBytes((long)next));
        }
        if (members is null) return;
        foreach (var owner in oldGroup) RemoveFromList(owner + list, member);
        add?.Invoke();
    }

    private nint? Locate(nint record, FieldLocation location) =>
        Follow(record, location.Path) is { } at ? at + (nint)GameDefinition.ParseOffset(location.Offset) : null;

    private readonly record struct ListState(nint Vector, nint Begin, int Count, int Capacity);

    private ListState? ReadListState(nint vector)
    {
        var begin = _memory.ReadPointer(vector);
        var end = _memory.ReadPointer(vector + 8);
        var cap = _memory.ReadPointer(vector + 16);
        if (begin == 0 || end < begin || cap < end || (cap - begin) / 8 > MaxListLength) return null;
        return new ListState(vector, begin, (int)((end - begin) / 8), (int)((cap - begin) / 8));
    }

    /// <summary>
    /// Works out how <paramref name="item"/> joins the list at <paramref name="vector"/>, without writing.
    /// A list with room takes it at the end. A full one swaps storage with a roomier list of the same
    /// kind (<see cref="MemberListDef.Donors"/>): both blocks were allocated by the game and each stays
    /// owned by exactly one list, so the game can grow or free them as usual. Growing a list in place
    /// would mean allocating inside the game, which the editor never does.
    /// </summary>
    private Action? PlanAdd(nint vector, nint item, MemberListDef members, HashSet<nint> exclude, string label)
    {
        var target = ReadListState(vector)
                     ?? throw new NotSupportedException($"{label}: the new squad list can't be read, so the move wasn't made.");
        var items = ReadPointerList(vector);
        if (items.Contains(item)) return null;

        if (target.Capacity > target.Count)
            return () =>
            {
                _memory.WriteChecked(target.Begin + target.Count * 8, BitConverter.GetBytes((long)item));
                _memory.WriteChecked(vector + 8, BitConverter.GetBytes((long)(target.Begin + (target.Count + 1) * 8)));
            };

        if (members.Donors is not { } donorKind || !Enum.TryParse<EntityKind>(donorKind, out var kind))
            throw new NotSupportedException($"{label}: the new squad list is full, so the move wasn't made.");
        var list = (nint)GameDefinition.ParseOffset(members.List);
        ListState? donor = null;
        foreach (var owner in GetAll(kind))
        {
            var v = owner.Address + list;
            if (v == vector || exclude.Contains(v) || ReadListState(v) is not { } d) continue;
            if (d.Capacity <= target.Count || d.Count > target.Capacity) continue;
            if (donor is null || d.Capacity < donor.Value.Capacity) donor = d;
        }
        if (donor is not { } give)
            throw new NotSupportedException(
                $"{label}: the new club's squad list is full and no spare list could be borrowed, so the move wasn't made. " +
                "Lists free up as the game plays on; try again in a few days.");

        return () =>
        {
            var donorItems = ReadPointerList(give.Vector);
            var mine = new byte[(target.Count + 1) * 8];
            for (var i = 0; i < target.Count; i++) BinaryPrimitives.WriteInt64LittleEndian(mine.AsSpan(i * 8), items[i]);
            BinaryPrimitives.WriteInt64LittleEndian(mine.AsSpan(target.Count * 8), item);

            _memory.WriteChecked(give.Begin, mine);
            if (donorItems.Count > 0)
            {
                var theirs = new byte[donorItems.Count * 8];
                for (var i = 0; i < donorItems.Count; i++) BinaryPrimitives.WriteInt64LittleEndian(theirs.AsSpan(i * 8), donorItems[i]);
                _memory.WriteChecked(target.Begin, theirs);
            }
            _memory.WriteChecked(vector, Triple(give.Begin, target.Count + 1, give.Capacity));
            _memory.WriteChecked(give.Vector, Triple(target.Begin, donorItems.Count, target.Capacity));
        };
    }

    private static byte[] Triple(nint begin, int count, int capacity)
    {
        var b = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(b, begin);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(8), begin + count * 8);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(16), begin + capacity * 8);
        return b;
    }

    private nint? Follow(nint addr, IEnumerable<string>? hops)
    {
        foreach (var hop in hops ?? [])
        {
            addr = _memory.ReadPointer(addr + (nint)GameDefinition.ParseOffset(hop));
            if (addr == 0) return null;
        }
        return addr;
    }

    private const int MaxListLength = 10_000;

    private List<nint> ReadPointerList(nint vector)
    {
        var begin = _memory.ReadPointer(vector);
        var end = _memory.ReadPointer(vector + 8);
        var count = (end - begin) / 8;
        if (begin == 0 || count <= 0 || count > MaxListLength) return [];
        var list = new List<nint>((int)count);
        for (var i = 0; i < count; i++) list.Add(_memory.ReadPointer(begin + i * 8));
        return list;
    }

    /// <summary>Takes <paramref name="item"/> out of a (begin, end, capacity) list in place, keeping the order.</summary>
    private void RemoveFromList(nint vector, nint item)
    {
        var items = ReadPointerList(vector);
        var index = items.IndexOf(item);
        if (index < 0) return;
        var begin = _memory.ReadPointer(vector);
        items.RemoveAt(index);
        var bytes = new byte[(items.Count + 1) * 8];
        for (var i = 0; i < items.Count; i++) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * 8), items[i]);
        _memory.WriteChecked(begin + index * 8, bytes.AsSpan(index * 8));
        _memory.WriteChecked(vector + 8, BitConverter.GetBytes((long)(begin + items.Count * 8)));
    }

    /// <summary>The field's address, or null when a pointer along its <see cref="FieldDef.Path"/> is empty.</summary>
    private nint? FieldAddress(nint recordBase, FieldDef field)
    {
        var addr = recordBase;
        if (field.Path is { } path)
        {
            foreach (var hop in path)
            {
                addr = _memory.ReadPointer(addr + (nint)GameDefinition.ParseOffset(hop));
                if (addr == 0) return null;
            }
        }
        return addr + (nint)GameDefinition.ParseOffset(field.Offset!);
    }

    private long ReadRaw(nint recordBase, FieldDef field) =>
        FieldAddress(recordBase, field) is { } addr ? (long)ReadNumber(addr, field.Type) : 0;

    private object? ReadField(nint recordBase, FieldDef field, ValueKind kind)
    {
        if (FieldAddress(recordBase, field) is not { } addr) return null;
        switch (field.Type)
        {
            case FieldType.FmDate:
                var day = _memory.ReadUInt16(addr) - _def.DateDayBase;
                var year = _memory.ReadUInt16(addr + 2);
                if (year is < 1 or > 9999 || day < 0) return null;
                if (_def.DateDayBase == 1 && year <= 1900) return null;
                return new DateOnly(year, 1, 1).AddDays(Math.Min(day, 365));
            case FieldType.FmString:
                var str = _memory.ReadPointer(addr);
                if (str == 0) return null;
                var length = _memory.ReadInt32(str);
                if (length <= 0 || length > field.MaxLength * 4) return length == 0 ? "" : null;
                return Encoding.UTF8.GetString(_memory.ReadBytes(str + 4, length));
            case FieldType.Utf16Ptr:
                return _memory.ReadString(_memory.ReadPointer(addr), Encoding.Unicode, field.MaxLength);
            case FieldType.Utf8Ptr:
                return _memory.ReadString(_memory.ReadPointer(addr), Encoding.UTF8, field.MaxLength);
            case FieldType.Utf16Inline:
                return _memory.ReadString(addr, Encoding.Unicode, field.MaxLength);
            case FieldType.EntityPtr:
                var p = _memory.ReadPointer(addr);
                return p != 0 && _byAddress.TryGetValue(p, out var e) ? e.Id : null;
        }

        if (field.Type == FieldType.Bool && !string.IsNullOrWhiteSpace(field.Mask))
            return (_memory.ReadByte(addr) & (byte)GameDefinition.ParseOffset(field.Mask)) != 0;

        if (field.Values is { Count: > 0 } stored)
        {
            var index = stored.IndexOf((long)ReadNumber(addr, field.Type));
            return index < 0 ? null : index;
        }

        var shown = ReadNumber(addr, field.Type) / field.Scale;
        return kind switch
        {
            ValueKind.Boolean => shown != 0,
            ValueKind.Money => (long)Math.Round(shown),
            ValueKind.Decimal => shown,
            ValueKind.Reference => shown < 0 ? null : (int)shown,
            _ => (int)Math.Round(shown),
        };
    }

    private double ReadNumber(nint addr, FieldType type) => type switch
    {
        FieldType.U8 or FieldType.Bool => _memory.ReadByte(addr),
        FieldType.I8 => (sbyte)_memory.ReadByte(addr),
        FieldType.U16 => _memory.ReadUInt16(addr),
        FieldType.I16 => _memory.ReadInt16(addr),
        FieldType.I32 => _memory.ReadInt32(addr),
        FieldType.U32 => _memory.ReadUInt32(addr),
        FieldType.I64 => _memory.ReadInt64(addr),
        FieldType.F32 => _memory.ReadSingle(addr),
        FieldType.F64 => _memory.ReadDouble(addr),
        _ => throw new NotSupportedException($"{type} is not numeric."),
    };

    private void WriteNumber(nint addr, FieldDef field, double raw)
    {
        var r = Math.Round(raw);
        byte[] bytes = field.Type switch
        {
            FieldType.U8 or FieldType.Bool => [(byte)Math.Clamp(r, 0, 255)],
            FieldType.I8 => [(byte)(sbyte)Math.Clamp(r, -128, 127)],
            FieldType.U16 => BitConverter.GetBytes((ushort)Math.Clamp(r, 0, ushort.MaxValue)),
            FieldType.I16 => BitConverter.GetBytes((short)Math.Clamp(r, short.MinValue, short.MaxValue)),
            FieldType.I32 => BitConverter.GetBytes((int)Math.Clamp(r, int.MinValue, int.MaxValue)),
            FieldType.U32 => BitConverter.GetBytes((uint)Math.Clamp(r, 0, uint.MaxValue)),
            FieldType.I64 => BitConverter.GetBytes((long)r),
            FieldType.F32 => BitConverter.GetBytes((float)raw),
            FieldType.F64 => BitConverter.GetBytes(raw),
            _ => throw new NotSupportedException($"{field.Type} is not numeric."),
        };
        _memory.WriteChecked(addr, bytes);
    }

    public void Dispose() => _owner?.Dispose();
}

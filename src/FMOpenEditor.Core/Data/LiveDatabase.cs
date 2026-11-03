using System.Buffers.Binary;
using System.Text;
using FMOpenEditor.Core.Definitions;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Model;

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
        foreach (var kind in Enum.GetValues<EntityKind>())
        {
            var list = new List<Entity>();
            var ids = new Dictionary<int, Entity>();
            if (_def.Collections.TryGetValue(kind.ToString(), out var col) && _roots.TryGetValue(col.Root, out var root))
            {
                try
                {
                    var idField = FieldsFor(kind)?.GetValueOrDefault("Id");
                    var index = 0;
                    foreach (var addr in EnumerateRecords(root, col))
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

    public object? Read(Entity entity, string key)
    {
        var field = Field(entity.Kind, key) ?? throw new NotSupportedException($"{entity.Kind}.{key} is not mapped for {_def.Version}.");
        return ReadField(entity.Address, field, PropertyCatalog.Get(entity.Kind, key).Kind);
    }

    public void Write(Entity entity, string key, object? value)
    {
        var field = Field(entity.Kind, key) ?? throw new NotSupportedException($"{entity.Kind}.{key} is not mapped for {_def.Version}.");
        var addr = FieldAddress(entity.Address, field);
        var prop = PropertyCatalog.Get(entity.Kind, key);

        switch (field.Type)
        {
            case FieldType.FmDate:
                var d = value as DateOnly? ?? default;
                var dateBytes = new byte[4];
                BinaryPrimitives.WriteUInt16LittleEndian(dateBytes, (ushort)(value is null ? 0 : d.DayOfYear - 1));
                BinaryPrimitives.WriteUInt16LittleEndian(dateBytes.AsSpan(2), (ushort)(value is null ? 0 : d.Year));
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
            case FieldType.EntityPtr:
                var refKind = prop.RefKind ?? throw new InvalidDataException($"{key} has no reference kind.");
                var ptr = value is int id && Find(refKind, id) is { } refEntity ? refEntity.Address : 0;
                _memory.WriteChecked(addr, BitConverter.GetBytes((long)ptr));
                break;
            default:
                WriteNumber(addr, field, value is null ? 0 : Values.ToDouble(value) * field.Scale);
                break;
        }
    }

    private nint FieldAddress(nint recordBase, FieldDef field)
    {
        var addr = recordBase;
        if (field.Path is { } path)
        {
            foreach (var hop in path) addr = _memory.ReadPointer(addr + (nint)GameDefinition.ParseOffset(hop));
        }
        return addr + (nint)GameDefinition.ParseOffset(field.Offset!);
    }

    private long ReadRaw(nint recordBase, FieldDef field) => (long)ReadNumber(FieldAddress(recordBase, field), field.Type);

    private object? ReadField(nint recordBase, FieldDef field, ValueKind kind)
    {
        var addr = FieldAddress(recordBase, field);
        switch (field.Type)
        {
            case FieldType.FmDate:
                var day = _memory.ReadUInt16(addr);
                var year = _memory.ReadUInt16(addr + 2);
                if (year is < 1 or > 9999) return null;
                return new DateOnly(year, 1, 1).AddDays(Math.Min(day, (ushort)365));
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

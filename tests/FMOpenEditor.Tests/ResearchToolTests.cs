using System.Text;
using FMOpenEditor.Core.Memory;
using FMOpenEditor.Core.Research;

namespace FMOpenEditor.Tests;

public class ResearchToolTests
{
    private static readonly nint Module = unchecked((nint)0x7FF6_0000_0000);
    private static readonly nint Heap = unchecked((nint)0x2_0000_0000);

    [Fact]
    public void Value_scanner_finds_doubles()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x1000);
        var b = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(b, 12_500_000.5);
        mem.Bytes((nint)(Heap + 0x40), b);

        var scanner = new MemoryScanner(mem);
        Assert.Equal(1, scanner.FirstScan(12_500_000.5, ScanValueType.Double));
        Assert.Equal((nint)(Heap + 0x40), scanner.Results[0]);
        Assert.True(scanner.WriteValue(scanner.Results[0], 3.25));
        Assert.Equal(3.25, mem.ReadDouble((nint)(Heap + 0x40)));
    }

    [Fact]
    public void Finds_strings_in_both_encodings()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x1000);
        mem.Utf16((nint)(Heap + 0x100), "Zébulon Okafor");
        mem.Bytes((nint)(Heap + 0x300), Encoding.UTF8.GetBytes("Zébulon Okafor\0"));

        Assert.Equal([(nint)(Heap + 0x100)], ReferenceScanner.FindString(mem, "Zébulon Okafor", Encoding.Unicode));
        Assert.Equal([(nint)(Heap + 0x300)], ReferenceScanner.FindString(mem, "Zébulon Okafor", Encoding.UTF8));
    }

    [Fact]
    public void Finds_a_string_that_straddles_a_chunk_boundary()
    {
        var size = RegionScan.ChunkSize + 0x1000;
        var mem = new FakeMemory().Map((nint)Heap, size);
        var at = (nint)(Heap + RegionScan.ChunkSize - 4);
        mem.Utf16(at, "Boundary");

        Assert.Equal([at], ReferenceScanner.FindString(mem, "Boundary", Encoding.Unicode));
    }

    [Fact]
    public void Finds_pointers_to_a_target_and_to_just_before_it()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x1000);
        var record = (nint)(Heap + 0x800);
        var field = record + 0x40;
        mem.I64((nint)(Heap + 0x10), field);   // exact
        mem.I64((nint)(Heap + 0x20), record);  // record start, 0x40 before
        mem.I64((nint)(Heap + 0x30), record - 0x1000); // too far

        var exact = ReferenceScanner.FindPointers(mem, field);
        Assert.Single(exact);
        Assert.Equal((nint)(Heap + 0x10), exact[0].Address);

        var near = ReferenceScanner.FindPointers(mem, field, maxOffset: 0x100);
        Assert.Equal(2, near.Count);
        Assert.Equal(0, near[0].Offset);
        Assert.Equal(0x40, near[1].Offset);
        Assert.Equal(record, near[1].Value);
    }

    /// <summary>Lays out MSVC x64 RTTI for one class, a vtable, and three objects in the heap.</summary>
    private static (FakeMemory Mem, nint Vtable, nint[] Objects) BuildRttiFixture()
    {
        var mem = new FakeMemory()
            .Map((nint)Module, 0x10000, writable: false)
            .Map((nint)Heap, 0x10000);

        // TypeDescriptor at +0x1000: vftable ptr, spare, name.
        var td = (nint)(Module + 0x1000);
        mem.I64(td, Module + 0x9000);
        mem.Bytes(td + 16, Encoding.ASCII.GetBytes(".?AVplayer@fm@@\0"));

        // CompleteObjectLocator at +0x2000.
        var col = (nint)(Module + 0x2000);
        mem.I32(col, 1);
        mem.I32(col + 4, 0);
        mem.I32(col + 8, 0);
        mem.I32(col + 12, 0x1000);
        mem.I32(col + 16, 0x3000);
        mem.I32(col + 20, 0x2000);

        // Vtable at +0x4008, COL pointer just before it.
        mem.I64((nint)(Module + 0x4000), col);
        var vtable = (nint)(Module + 0x4008);
        mem.I64(vtable, Module + 0x5000);

        // An unrelated type descriptor without a vtable.
        mem.I64((nint)(Module + 0x1100), Module + 0x9000);
        mem.Bytes((nint)(Module + 0x1110), Encoding.ASCII.GetBytes(".?AUunused@@\0"));

        var objects = new[] { (nint)(Heap + 0x100), (nint)(Heap + 0x400), (nint)(Heap + 0x900) };
        foreach (var o in objects) mem.I64(o, vtable);
        return (mem, vtable, objects);
    }

    [Fact]
    public void Rtti_finds_classes_and_vtables()
    {
        var (mem, vtable, _) = BuildRttiFixture();
        var classes = RttiScanner.FindClasses(mem, (nint)Module, 0x10000);

        var player = Assert.Single(classes, c => c.Name == "fm::player");
        Assert.Equal(".?AVplayer@fm@@", player.MangledName);
        Assert.Equal(vtable, player.PrimaryVtable!.Address);
        Assert.Equal(0, player.PrimaryVtable.ObjectOffset);

        var unused = Assert.Single(classes, c => c.Name == "unused");
        Assert.Empty(unused.Vtables);
    }

    [Fact]
    public void Rtti_finds_every_object_of_a_class_in_the_heap()
    {
        var (mem, vtable, objects) = BuildRttiFixture();
        var result = RttiScanner.FindInstances(mem, [vtable], (nint)Module, 0x10000);

        Assert.Equal(3, result.Counts[vtable]);
        Assert.Equal(objects, result.Samples[vtable]);
    }

    [Fact]
    public void Rtti_object_bases_account_for_the_vtable_offset()
    {
        var (mem, vtable, _) = BuildRttiFixture();
        // A class whose only vtable sits 0x100 into the object (e.g. a virtual base).
        var obj = Heap + 0x3000;
        mem.I64(obj + 0x100, vtable);
        var result = RttiScanner.FindInstances(mem, [new RttiVtable(vtable, 0, 0x100)], Module, 0x10000);
        Assert.Contains(obj, result.Samples[vtable]);
    }

    [Fact]
    public void Pe_file_maps_sections_at_the_image_base()
    {
        // Minimal PE32+: headers in the first 0x200 bytes, one section at RVA 0x1000 from file offset 0x200.
        var file = new byte[0x400];
        file[0] = (byte)'M'; file[1] = (byte)'Z';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0x3C), 0x80);
        var pe = 0x80;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(pe), 0x4550);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(pe + 6), 1);      // sections
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(pe + 20), 0xF0);  // optional header size
        var opt = pe + 24;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(opt), 0x20B);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(opt + 24), 0x180000000);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(opt + 56), 0x3000);
        var sec = opt + 0xF0;
        Encoding.ASCII.GetBytes(".data").CopyTo(file, sec);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sec + 8), 0x2000);   // virtual size (bigger than raw)
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sec + 12), 0x1000);  // RVA
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sec + 16), 0x200);   // raw size
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sec + 20), 0x200);   // raw offset
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(sec + 36), 0xC0000040);
        Encoding.ASCII.GetBytes("hello").CopyTo(file, 0x210);

        var mem = new PeFileMemory(file);
        Assert.Equal(unchecked((nint)0x180000000), mem.ImageBase);
        Assert.Equal("hello", mem.ReadString(unchecked((nint)0x180001010), Encoding.ASCII, 5));
        Assert.Equal(0, mem.ReadByte(unchecked((nint)0x180002800))); // past raw data, inside the virtual size: zero
        Assert.False(mem.Read(unchecked((nint)0x180005000), new byte[1]));
        Assert.Equal([unchecked((nint)0x180001010)], ReferenceScanner.FindString(mem, "hello", Encoding.ASCII));
    }

    [Theory]
    [InlineData(".?AVplayer@@", "player")]
    [InlineData(".?AVclub@fm@sigames@@", "sigames::fm::club")]
    [InlineData(".?AUpoint@@", "point")]
    [InlineData(".?AV?$vector@H@std@@", "?$vector@H@std")]
    public void Demangles_simple_class_names(string mangled, string expected) =>
        Assert.Equal(expected, RttiScanner.Demangle(mangled));

    [Fact]
    public void Diff_reports_changed_fields_with_readings()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x1000);
        var rec = (nint)(Heap + 0x200);
        mem.I32(rec + 0x10, 1200);
        mem.Bytes(rec + 0x21, 60);
        var before = RecordSnapshot.Take(mem, rec, 0x40);

        mem.I32(rec + 0x10, 1500);
        mem.Bytes(rec + 0x21, 65);
        var after = RecordSnapshot.Take(mem, rec, 0x40);

        var changes = RecordDiff.Compare(before, after);
        Assert.Equal(2, changes.Count);
        Assert.Equal(0x10, changes[0].Offset);
        Assert.Contains("i32 @0x10: 1200 → 1500", changes[0].Readings);
        Assert.Equal(0x21, changes[1].Offset);
        Assert.Contains("u8 @0x21: 60 → 65", changes[1].Readings);

        var rows = RecordDiff.HexDump(after, before);
        Assert.Equal(4, rows.Count);
        Assert.True(rows[1].Changed);
        Assert.True(rows[2].Changed);
        Assert.False(rows[0].Changed);
    }

    [Fact]
    public void Diff_reads_fm_dates()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x100);
        mem.I16((nint)Heap, 31); mem.I16((nint)Heap + 2, 2027);
        var a = RecordSnapshot.Take(mem, (nint)Heap, 8);
        mem.I16((nint)Heap, 32);
        var b = RecordSnapshot.Take(mem, (nint)Heap, 8);

        var change = Assert.Single(RecordDiff.Compare(a, b));
        Assert.Contains("fmDate @0x0: 2027-02-01 → 2027-02-02", change.Readings);
    }

    [Fact]
    public void Snapshot_survives_an_unreadable_page()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x1000); // next page unmapped
        var snap = RecordSnapshot.Take(mem, (nint)(Heap + 0xF00), 0x200);
        Assert.True(snap.IsReadable(0, 0x100));
        Assert.False(snap.IsReadable(0x100, 1));
    }

    [Fact]
    public void Lists_pointer_fields_with_string_previews()
    {
        var (mem, vtable, objects) = BuildRttiFixture();
        var rec = objects[0];
        mem.Utf16((nint)(Heap + 0x2000), "Okafor");
        mem.I64(rec + 0x18, Heap + 0x2000);

        var fields = RecordDiff.Pointers(mem, RecordSnapshot.Take(mem, rec, 0x40), (nint)Module, 0x10000);
        Assert.Contains(fields, f => f.Offset == 0 && f.Target == vtable && f.Description.StartsWith("module+0x4008"));
        Assert.Contains(fields, f => f.Offset == 0x18 && f.Description == "utf16 \"Okafor\"");
    }

    /// <summary>Ten fake players: Finishing stored ×5 at 0x41 (1–100 scale), wage as i32 at 0x48.</summary>
    private static (FakeMemory Mem, nint[] Bases, int[] Finishing, int[] Wage) BuildPlayers()
    {
        var mem = new FakeMemory().Map((nint)Heap, 0x10000);
        int[] finishing = [15, 7, 18, 12, 3, 20, 9, 11, 14, 1];
        int[] wage = [12000, 800, 95000, 4300, 150, 210000, 2500, 7700, 18000, 90];
        var rng = new Random(42);
        var bases = new nint[finishing.Length];
        for (var i = 0; i < bases.Length; i++)
        {
            var b = (nint)(Heap + 0x400 * i);
            bases[i] = b;
            var noise = new byte[0x100];
            rng.NextBytes(noise);
            mem.Bytes(b, noise);
            mem.Bytes(b + 0x41, (byte)(finishing[i] * 5));
            mem.I32(b + 0x48, wage[i]);
        }
        return (mem, bases, finishing, wage);
    }

    [Fact]
    public void Correlator_finds_a_scaled_attribute()
    {
        var (mem, bases, finishing, _) = BuildPlayers();
        var samples = bases.Select((b, i) => new CorrelationSample(b, finishing[i])).ToList();

        var result = FieldCorrelator.Correlate(mem, samples, 0x100);
        var top = result.Candidates[0];
        Assert.Equal(0x41, top.Offset);
        Assert.Equal("u8", top.Type);
        Assert.Equal(5, top.Scale);
        Assert.Equal(CorrelationKind.Exact, top.Kind);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Correlator_finds_a_rounded_value()
    {
        var (mem, bases, finishing, _) = BuildPlayers();
        // Store 1–100 values that aren't exact multiples of 5: shown = round(stored / 5).
        for (var i = 0; i < bases.Length; i++) mem.Bytes(bases[i] + 0x41, (byte)(finishing[i] * 5 - (i % 2 == 0 ? 2 : -2)));
        var samples = bases.Select((b, i) => new CorrelationSample(b, finishing[i])).ToList();

        var top = FieldCorrelator.Correlate(mem, samples, 0x100).Candidates[0];
        Assert.Equal(0x41, top.Offset);
        Assert.Equal(CorrelationKind.Rounded, top.Kind);
        Assert.Equal(5, top.Scale);
    }

    [Fact]
    public void Correlator_finds_an_unscaled_int()
    {
        var (mem, bases, _, wage) = BuildPlayers();
        var samples = bases.Select((b, i) => new CorrelationSample(b, wage[i])).ToList();

        var top = FieldCorrelator.Correlate(mem, samples, 0x100).Candidates[0];
        Assert.Equal(0x48, top.Offset);
        Assert.Equal(1, top.Scale);
        Assert.Equal("stored = shown", top.Formula);
    }

    [Fact]
    public void Correlator_warns_when_values_dont_vary()
    {
        var (mem, bases, _, _) = BuildPlayers();
        var samples = bases.Take(3).Select(b => new CorrelationSample(b, 10)).ToList();
        var result = FieldCorrelator.Correlate(mem, samples, 0x100);
        Assert.Contains(result.Warnings, w => w.Contains("distinct"));
    }
}

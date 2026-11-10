using System.Buffers.Binary;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Memory;

namespace FMOpenEditor.UiTests;

/// <summary>
/// Drives every Research tool against a fake game process: a module with RTTI for one class, and
/// twelve objects of it in the heap. Screenshots go to $RESEARCH_SHOTS when it is set.
/// </summary>
public class ResearchViewTests
{
    private static readonly nint Module = unchecked((nint)0x7FF6_0000_0000);
    private static readonly nint Heap = unchecked((nint)0x2_0000_0000);
    private static readonly int[] Finishing = [15, 7, 18, 12, 3, 20, 9, 11, 14, 1, 16, 5];

    private sealed class Fake : IMemoryAccess
    {
        private readonly List<(nint Start, byte[] Data, bool Writable)> _regions = [];
        public Fake Map(nint start, int size, bool writable = true) { _regions.Add((start, new byte[size], writable)); return this; }

        private (byte[], int)? At(nint a, int n)
        {
            foreach (var (s, d, _) in _regions) if (a >= s && a + n <= s + d.Length) return (d, (int)(a - s));
            return null;
        }

        public bool Read(nint a, Span<byte> buf) { if (At(a, buf.Length) is not var (d, o)) return false; d.AsSpan(o, buf.Length).CopyTo(buf); return true; }
        public bool Write(nint a, ReadOnlySpan<byte> b) { if (At(a, b.Length) is not var (d, o)) return false; b.CopyTo(d.AsSpan(o)); return true; }
        public IEnumerable<MemoryRegion> Regions() => _regions.OrderBy(r => r.Start).Select(r => new MemoryRegion(r.Start, r.Data.Length, r.Writable));
        public void I32(nint a, int v) { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, v); Write(a, b); }
        public void I64(nint a, long v) { var b = new byte[8]; BinaryPrimitives.WriteInt64LittleEndian(b, v); Write(a, b); }
    }

    private static (Fake Mem, nint[] Players) BuildGame()
    {
        var mem = new Fake().Map(Module, 0x10000, writable: false).Map(Heap, 0x20000);
        var td = Module + 0x1000;
        mem.I64(td, Module + 0x9000);
        mem.Write(td + 16, Encoding.ASCII.GetBytes(".?AVplayer@fm@@\0"));
        mem.I64(Module + 0x1100, Module + 0x9000);
        mem.Write(Module + 0x1110, Encoding.ASCII.GetBytes(".?AVclub@fm@@\0"));
        var col = Module + 0x2000;
        mem.I32(col, 1); mem.I32(col + 12, 0x1000); mem.I32(col + 16, 0x3000); mem.I32(col + 20, 0x2000);
        mem.I64(Module + 0x4000, col);
        var vtable = Module + 0x4008;
        // A global in the module pointing at the player array: the root to discover.
        mem.I64(Module + 0x8000, Heap + 0x10000);

        var rng = new Random(7);
        var players = new nint[Finishing.Length];
        for (var i = 0; i < players.Length; i++)
        {
            var p = Heap + 0x800 * i;
            players[i] = p;
            var noise = new byte[0x100];
            rng.NextBytes(noise);
            mem.Write(p, noise);
            mem.I64(p, vtable);
            mem.Write(p + 0x41, [(byte)(Finishing[i] * 5)]);
            mem.I64(p + 0x18, Heap + 0x8000 + 0x40 * i);
            mem.Write(Heap + 0x8000 + 0x40 * i, Encoding.Unicode.GetBytes($"Player {i} Okafor\0"));
            mem.I64(Heap + 0x10000 + 8 * i, p);
        }
        return (mem, players);
    }

    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, string name)
    {
        Pump();
        var dir = Environment.GetEnvironmentVariable("RESEARCH_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir, name + ".png"));
    }

    [AvaloniaFact]
    public async Task Every_research_tool_works_against_a_fake_game()
    {
        var (mem, players) = BuildGame();
        var vm = new ResearchViewModel(mem, "fm.exe (pid 1) · game_plugin.dll at 0x7FF600000000, 0 MB", Module, 0x10000);
        var w = new Window { Content = new ResearchView { DataContext = vm }, Width = 1280, Height = 1000 };
        w.Show();
        Pump();

        // RTTI: find classes, count players, open one as a record.
        vm.SelectedTab = 2;
        await vm.Rtti!.FindClassesCommand.ExecuteAsync(null);
        Assert.Equal(["fm::club", "fm::player"], vm.Rtti.Classes.Select(c => c.Name));
        vm.Rtti.Filter = "player";
        vm.Rtti.SelectedClass = vm.Rtti.Classes.Single();
        await vm.Rtti.CountSelectedCommand.ExecuteAsync(null);
        Assert.Equal("12 objects", vm.Rtti.Classes.Single().InstancesText);
        Assert.Equal(players, vm.Rtti.Instances.Select(i => i.Address));
        Shot(w, "1-rtti");

        foreach (var inst in vm.Rtti.Instances)
        {
            vm.Rtti.SelectedInstance = inst;
            vm.Rtti.AddInstanceToCorrelatorCommand.Execute(null);
        }
        vm.Rtti.SelectedInstance = vm.Rtti.Instances[0];
        vm.Rtti.UseInstanceAsBaseCommand.Execute(null);
        Assert.Equal(3, vm.SelectedTab);

        // Record diff: snapshot, change Finishing in the "game", snapshot again.
        vm.Diff!.SnapshotCommand.Execute(null);
        mem.Write(players[0] + 0x41, [100]);
        vm.Diff.SnapshotCommand.Execute(null);
        var change = Assert.Single(vm.Diff.Changes);
        Assert.Equal("0x41", change.OffsetText);
        Assert.Contains("u8 @0x41: 75 → 100", change.Readings);
        Assert.Contains(vm.Diff.Pointers, p => p.OffsetText == "0x18" && p.Description.Contains("Player 0 Okafor"));
        Assert.Contains(vm.Diff.Pointers, p => p.OffsetText == "0x0" && p.Description.StartsWith("module+0x4008"));
        Shot(w, "2-diff");
        mem.Write(players[0] + 0x41, [75]);

        // Correlator: fill in the shown values next to the addresses the RTTI tab added.
        vm.SelectedTab = 4;
        var lines = vm.Correlator!.SamplesText.Split(Environment.NewLine);
        Assert.Equal(12, lines.Length);
        vm.Correlator.SamplesText = string.Join(Environment.NewLine, lines.Select((l, i) => l.Trim() + " " + Finishing[i]));
        await vm.Correlator.CorrelateCommand.ExecuteAsync(null);
        var top = vm.Correlator.Candidates[0];
        Assert.Equal("0x41", top.OffsetText);
        Assert.Equal("u8", top.Type);
        Assert.Contains("\"scale\": 5", top.Json);
        Shot(w, "3-correlator");

        // Strings & pointers: find a name, then climb from a player to the global root.
        vm.SelectedTab = 1;
        vm.Search!.Text = "Player 3 Okafor";
        await vm.Search.FindStringCommand.ExecuteAsync(null);
        Assert.Equal(Heap + 0x8000 + 0xC0, Assert.Single(vm.Search.Results).Address);

        vm.Search.PointerTarget = $"0x{(long)players[0]:X}";
        await vm.Search.FindPointersCommand.ExecuteAsync(null);
        var arraySlot = Assert.Single(vm.Search.Results);
        Assert.Equal(Heap + 0x10000, arraySlot.Address);
        vm.Search.PointerTarget = arraySlot.AddressText;
        await vm.Search.FindPointersCommand.ExecuteAsync(null);
        var root = Assert.Single(vm.Search.Results);
        Assert.Equal("game_plugin.dll+0x8000", root.AddressText);
        Assert.Contains("★", root.Detail);
        Shot(w, "4-pointers");

        // Value scan tab still works, now with Double.
        vm.SelectedTab = 0;
        Assert.Contains(ScanValueType.Double, vm.Types);
        Shot(w, "0-value-scan");
    }
}

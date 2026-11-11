using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.UiTests;

/// <summary>
/// Captures the README screenshots from a real, running Football Manager save. Does nothing unless
/// LIVE_SCREENSHOTS=1 and the game is running with a save loaded. It only reads: nothing is saved
/// into the game.
/// </summary>
public class LiveScreenshotTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void PumpUntil(Func<bool> done, int seconds = 120)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!done() && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
            Pump();
        }
    }

    private static void Settle()
    {
        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(25);
            Pump();
        }
    }

    private static void Theme(bool night) => AppServices.ApplyTheme(night ? ThemeChoice.Night : ThemeChoice.Day);

    private static void Shot(Window w, string name)
    {
        Settle();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        var outDir = Path.Combine(dir!.FullName, "docs", "screenshots");
        Directory.CreateDirectory(outDir);
        w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
    }

    private static void Pick(Window w, int index = 0)
    {
        var grid = w.GetVisualDescendants().OfType<DataGrid>().First();
        grid.SelectedIndex = index;
        Pump();
    }

    private static void Tab(BrowserViewModel page, params string[] names)
    {
        var tab = names.Select(n => page.Editor!.Tabs.FirstOrDefault(t => t.Name == n)).FirstOrDefault(t => t is not null);
        if (tab is not null) page.Editor!.SelectedGroup = tab;
        Pump();
    }

    private static BrowserViewModel Go(MainWindowViewModel vm, string title)
    {
        vm.SelectedNav = vm.Nav.Single(n => n.Title == title);
        Pump();
        return Assert.IsType<BrowserViewModel>(vm.CurrentPage);
    }

    [AvaloniaFact]
    public void Capture_readme_screenshots_from_the_running_game()
    {
        if (Environment.GetEnvironmentVariable("LIVE_SCREENSHOTS") != "1") return;

        var settings = AppServices.Settings;
        settings.ShowPictures = true;
        var vm = new MainWindowViewModel { AutoSave = false };
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();

        Theme(night: true);
        Shot(w, "home");
        Theme(night: false);
        Shot(w, "home-day");

        var connect = vm.ConnectGameCommand.ExecuteAsync(null);
        PumpUntil(() => connect.IsCompleted);
        Assert.True(vm.Session?.Database.IsLive, vm.ConnectMessage ?? "Not connected.");
        var images = ImageService.Instance;
        PumpUntil(() => !images.IsIndexing, 300);

        // Players, best first, with faces from the graphics pack.
        Theme(night: true);
        var players = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Pick(w);
        Tab(players, "Technical", "Attributes");
        Shot(w, "players");

        // A preset staged on the top player: pending edits glow amber until saved (they never are here).
        Theme(night: false);
        Tab(players, "Mental");
        players.ApplyPresetCommand.Execute(vm.Presets.Presets.Single(p => p.Name == "Make WonderKid"));
        Shot(w, "player-editor");
        vm.DiscardCommand.Execute(null);
        Pump();
        Assert.Equal(0, vm.PendingCount);

        // Advanced search: teenagers.
        Theme(night: true);
        players.ShowFilters = true;
        Pump();
        players.AddFilterCommand.Execute(null);
        Pump();
        players.Filters[0].Op = OpOption.All.Single(o => o.Op == Core.Search.FilterOp.AtMost);
        players.Filters[0].Value = "19";
        players.SearchCommand.Execute(null);
        Pump();
        Pick(w);
        Shot(w, "advanced-search");

        // Clubs: finances, then the squad.
        Theme(night: false);
        var clubs = Go(vm, "Clubs");
        Pick(w);
        Tab(clubs, "Finances");
        Shot(w, "club-finances");
        Theme(night: true);
        Tab(clubs, "Players");
        Shot(w, "club-squad");

        // A competition and its clubs.
        Theme(night: false);
        var comps = Go(vm, "Competitions");
        Pick(w);
        Tab(comps, "Clubs");
        Shot(w, "competition");

        Assert.Equal(0, vm.PendingCount);
        vm.DisconnectCommand.Execute(null);
        Theme(night: true);
        w.Close();
    }
}

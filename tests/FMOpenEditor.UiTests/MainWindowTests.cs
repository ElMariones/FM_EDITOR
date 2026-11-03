using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.UiTests;

/// <summary>Drives the real window against the demo save. Screenshots land in docs/screenshots when SCREENSHOTS=1.</summary>
public class MainWindowTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, string name)
    {
        Pump();
        if (Environment.GetEnvironmentVariable("SCREENSHOTS") != "1") return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "docs"))) dir = dir.Parent;
        var outDir = Path.Combine(dir!.FullName, "docs", "screenshots");
        Directory.CreateDirectory(outDir);
        w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
    }

    [AvaloniaFact]
    public void Home_then_demo_then_edit_preset_and_save()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        Shot(w, "home");

        vm.OpenDemoCommand.Execute(null);
        Pump();
        var browser = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Assert.Equal(EntityKind.Player, browser.Kind);
        Assert.True(browser.Rows.Count > 1000);

        var grid = w.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal(browser.Columns.Count, grid.Columns.Count);

        grid.SelectedIndex = 0;
        Pump();
        Assert.NotNull(browser.Editor);
        var first = browser.Rows[0];
        Assert.Equal(first.Entity, browser.Editor!.Entity);
        Assert.False(string.IsNullOrEmpty(first[PropertyCatalog.DisplayName]));
        Shot(w, "players");

        // Edit through the generated editor, as the user would.
        var finishing = browser.Editor.Groups.SelectMany(g => g.Editors).OfType<NumberEditorViewModel>()
            .Single(e => e.Def.Key == "Attr.Finishing");
        finishing.Value = 20;
        Assert.True(finishing.IsPending);
        Assert.Equal(1, vm.PendingCount);

        browser.ApplyPresetCommand.Execute(vm.Presets.Presets.Single(p => p.Name == "Make WonderKid"));
        Assert.True(vm.PendingCount > 1);
        browser.Editor.SelectedGroup = browser.Editor.Groups.Single(g => g.Name == "Technical");
        Shot(w, "player-editor");

        vm.SaveCommand.Execute(null);
        Pump();
        Assert.Equal(0, vm.PendingCount);
        Assert.Equal(20, vm.Session!.Database.Read(first.Entity, "Attr.Finishing"));

        // Advanced search: young high-potential players.
        browser.ShowFilters = true;
        Pump();
        browser.AddFilterCommand.Execute(null);
        Pump();
        browser.Filters[0].Op = OpOption.All.Single(o => o.Op == Core.Search.FilterOp.AtMost);
        browser.Filters[0].Value = "19";
        Pump();
        browser.SearchCommand.Execute(null);
        Pump();
        Assert.All(browser.Rows, r => Assert.True(int.Parse(r["Age"]) <= 19));
        Shot(w, "advanced-search");

        // Clubs page.
        vm.SelectedNav = vm.Nav.Single(n => n.Title == "Clubs");
        Pump();
        var clubs = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        var clubGrid = w.GetVisualDescendants().OfType<DataGrid>().Single();
        clubGrid.SelectedIndex = 0;
        Pump();
        clubs.Editor!.SelectedGroup = clubs.Editor.Groups.Single(g => g.Name == "Finances");
        Shot(w, "club-finances");

        w.Close();
    }
}

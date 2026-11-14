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

/// <summary>Drives the real window against the demo save. Screenshots land in docs/screenshots/demo when SCREENSHOTS=1.</summary>
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
        var outDir = Path.Combine(dir!.FullName, "docs", "screenshots", "demo");
        Directory.CreateDirectory(outDir);
        w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
    }

    [AvaloniaFact]
    public void Copy_and_paste_stats_are_small_icon_buttons_that_leave_the_name_room()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();
        var browser = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        var grid = w.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedIndex = 0;
        Pump();

        var editor = w.GetVisualDescendants().OfType<EntityEditorView>().Single();
        var copy = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CopyStatsButton");
        var paste = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PasteStatsButton");
        Assert.True(copy.IsVisible && copy.Bounds.Width <= 32);
        Assert.False(paste.IsEnabled);
        var title = editor.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("h1"));
        Assert.True(title.Bounds.Width >= 120, $"the name only got {title.Bounds.Width}px");

        browser.Editor!.CopyStatsCommand.Execute(null);
        Pump();
        Assert.True(paste.IsEnabled);

        var shot = Environment.GetEnvironmentVariable("EDITOR_SHOT");
        if (!string.IsNullOrEmpty(shot)) w.CaptureRenderedFrame()?.Save(shot);
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

    [AvaloniaFact]
    public void Links_between_players_clubs_and_nations()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();

        // Player → club, as a double-click on the club cell does.
        var players = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        var player = players.Rows.First(r => players.ReferenceAt(r, "Contract.Club") is not null);
        var club = players.ReferenceAt(player, "Contract.Club")!;
        players.OpenReference(player, "Contract.Club");
        Pump();
        var clubs = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Assert.Equal(EntityKind.Club, clubs.Kind);
        Assert.Equal(club, clubs.Editor!.Entity);
        Assert.True(vm.CanGoBack);
        var clubGrid = w.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal(club, Assert.IsType<EntityRow>(clubGrid.SelectedItem).Entity);

        // The club's squad lists the player, and clicking the row opens them again.
        var squad = Assert.IsType<RelatedListViewModel>(clubs.Editor.Tabs.Single(t => t.Name == "Players"));
        clubs.Editor.SelectedGroup = squad;
        Shot(w, "club-squad");
        var row = squad.Items.Single(i => i.Entity == player.Entity);
        squad.OpenCommand.Execute(row);
        Pump();
        Assert.Same(players, vm.CurrentPage);
        Assert.Equal(player.Entity, players.Editor!.Entity);

        // Club → nation through the editor's reference field.
        vm.GoBackCommand.Execute(null);
        Pump();
        Assert.Same(clubs, vm.CurrentPage);
        Assert.Equal(club, clubs.Editor!.Entity);
        var nationField = clubs.Editor.Groups.SelectMany(g => g.Editors).OfType<ReferenceEditorViewModel>().Single(e => e.Def.Key == "Nation");
        nationField.OpenCommand.Execute(null);
        Pump();
        var nations = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Assert.Equal(EntityKind.Nation, nations.Kind);
        var nationClubs = Assert.IsType<RelatedListViewModel>(nations.Editor!.Tabs.Single(t => t.Name == "Clubs"));
        Assert.Contains(nationClubs.Items, i => i.Entity == club);

        w.Close();
    }

    [AvaloniaFact]
    public void Lists_open_best_first_and_history_steps_back_and_forward()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();

        // Players arrive sorted by current ability, highest first, and the header says so.
        var players = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Assert.Equal("CurrentAbility", players.SortKey);
        var ca = players.Rows.Select(r => Convert.ToDouble(r.Raw("CurrentAbility"))).ToList();
        Assert.Equal(ca.OrderByDescending(x => x), ca);
        var grid = w.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal("CA ↓", grid.Columns.Single(c => (string)c.Tag! == "CurrentAbility").Header);
        Assert.True(grid.Columns.Single(c => (string)c.Tag! == "Age").ActualWidth <= 50);

        // Opening a row, then another page, are both steps back.
        grid.SelectedIndex = 0;
        Pump();
        var top = players.Editor!.Entity;
        Assert.False(vm.CanGoBack); // the first pick replaces the empty page
        vm.SelectedNav = vm.Nav.Single(n => n.Title == "Clubs");
        Pump();
        var clubs = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Assert.Equal("Finance.Balance", clubs.SortKey);
        var balance = clubs.Rows.Select(r => Convert.ToDouble(r.Raw("Finance.Balance"))).ToList();
        Assert.Equal(balance.OrderByDescending(x => x), balance);
        Assert.True(vm.CanGoBack);

        // The mouse's back button returns to the player; forward goes to clubs again.
        w.MouseDown(new Avalonia.Point(700, 400), MouseButton.XButton1);
        w.MouseUp(new Avalonia.Point(700, 400), MouseButton.XButton1);
        Pump();
        Assert.Same(players, vm.CurrentPage);
        Assert.Equal(top, players.Editor!.Entity);
        Assert.True(vm.CanGoForward);
        w.MouseDown(new Avalonia.Point(700, 400), MouseButton.XButton2);
        w.MouseUp(new Avalonia.Point(700, 400), MouseButton.XButton2);
        Pump();
        Assert.Same(clubs, vm.CurrentPage);
        Assert.False(vm.CanGoForward);

        w.Close();
    }
}

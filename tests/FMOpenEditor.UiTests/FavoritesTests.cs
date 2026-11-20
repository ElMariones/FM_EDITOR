using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.UiTests;

public class FavoritesTests
{
    private static void Pump()
    {
        for (var i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Starred_players_are_listed_on_Favorites_and_kept_on_disk()
    {
        AppServices.Settings.FavoritePlayers.Clear();
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1640, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();
        var playersNav = vm.SelectedNav;
        // Open Favorites first (empty), as a user would, so the page already exists when the star is clicked.
        vm.SelectedNav = vm.Nav.Single(n => n.Title == "Favorites");
        Pump();
        Assert.Empty(Assert.IsType<BrowserViewModel>(vm.CurrentPage).Rows);
        vm.SelectedNav = playersNav;
        Pump();
        var players = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        w.GetVisualDescendants().OfType<DataGrid>().Single().SelectedIndex = 0;
        Pump();
        var editor = players.Editor!;
        Assert.False(editor.IsFavorite);

        editor.ToggleFavoriteCommand.Execute(null);
        Assert.True(editor.IsFavorite);
        Assert.Equal([editor.Entity.Id], AppServices.Settings.FavoritePlayers);

        vm.SelectedNav = vm.Nav.Single(n => n.Title == "Favorites");
        Pump();
        var favorites = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        Assert.Equal([editor.Entity], favorites.Rows.Select(r => r.Entity));

        w.GetVisualDescendants().OfType<DataGrid>().Single().SelectedIndex = 0;
        Pump();
        Assert.True(favorites.Editor!.IsFavorite);
        if (Environment.GetEnvironmentVariable("SHOT") is { } shot) w.CaptureRenderedFrame()?.Save(shot);
        editor.ToggleFavoriteCommand.Execute(null);
        Assert.Empty(AppServices.Settings.FavoritePlayers);
        w.Close();
    }

    [Fact]
    public void Favorites_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fmoe-settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = AppSettings.Load(path);
            s.FavoritePlayers.AddRange([7, 42]);
            s.Save();
            Assert.Equal([7, 42], AppSettings.Load(path).FavoritePlayers);
        }
        finally { File.Delete(path); }
    }
}

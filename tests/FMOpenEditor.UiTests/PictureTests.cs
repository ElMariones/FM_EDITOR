using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.Controls;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.UiTests;

/// <summary>
/// Faces and badges from a graphics pack, end to end: a pack is generated on disk in the same
/// format the game uses, the demo save is pointed at it, and the window is checked (and captured
/// in the Day theme when SCREENSHOTS=1).
/// </summary>
public class PictureTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void PumpUntil(Func<bool> done)
    {
        for (var i = 0; i < 400 && !done(); i++)
        {
            Thread.Sleep(10);
            Pump();
        }
        Assert.True(done(), "Timed out waiting for pictures.");
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

    /// <summary>A stand-in picture: Tiki, tinted by id so neighbours look different.</summary>
    private static void SavePicture(string path, int seed)
    {
        const int size = 96;
        var mascot = new Mascot { Badge = seed % 3 == 0, Warmth = seed % 5 / 4.0, Width = size, Height = size, FollowPointer = false };
        mascot.Measure(new Size(size, size));
        mascot.Arrange(new Rect(0, 0, size, size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        bitmap.Render(mascot);
        bitmap.Save(path);
    }

    private static void WritePack(string dir, string type, string slot, string prefix, IEnumerable<int> ids)
    {
        Directory.CreateDirectory(dir);
        var records = new List<string>();
        foreach (var id in ids)
        {
            SavePicture(Path.Combine(dir, $"{prefix}{id}.png"), id);
            records.Add($"\t\t<record from=\"{prefix}{id}\" to=\"graphics/pictures/{type}/{id}/{slot}\"/>");
        }
        File.WriteAllText(Path.Combine(dir, "config.xml"),
            "<record>\n\t<boolean id=\"preload\" value=\"false\"/>\n\t<list id=\"maps\">\n" + string.Join('\n', records) + "\n\t</list>\n</record>");
    }

    [AvaloniaFact]
    public void Settings_panel_switches_theme_and_reports_the_graphics_folder()
    {
        var vm = new MainWindowViewModel();
        var w = new Window { Width = 420, Height = 520, Content = new SettingsPanel { DataContext = vm.Settings, Margin = new Thickness(14) } };
        w.Show();
        try
        {
            vm.Settings.ThemeIndex = (int)ThemeChoice.Day;
            Pump();
            Assert.Equal(ThemeVariant.Light, Application.Current!.ActualThemeVariant);
            Assert.False(vm.IsNight);

            vm.ToggleThemeCommand.Execute(null);
            Pump();
            Assert.Equal(ThemeVariant.Dark, Application.Current.ActualThemeVariant);
            Assert.Equal((int)ThemeChoice.Night, vm.Settings.ThemeIndex);

            Assert.False(string.IsNullOrWhiteSpace(vm.Settings.FolderSummary));
            Assert.False(string.IsNullOrWhiteSpace(vm.Settings.LibrarySummary));
            // No screenshot here: the panel shows this machine's own folder path.
        }
        finally
        {
            AppServices.Settings.Theme = ThemeChoice.Night;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            w.Close();
        }
    }

    [AvaloniaFact]
    public void Pictures_show_where_a_pack_has_them_and_collapse_where_it_does_not()
    {
        var root = Path.Combine(Path.GetTempPath(), "fmoe-ui-graphics-" + Guid.NewGuid().ToString("N"));
        var settings = AppServices.Settings;
        try
        {
            var vm = new MainWindowViewModel();
            // The demo save is opened once without pictures to learn which ids it uses.
            vm.OpenDemoCommand.Execute(null);
            var players = vm.Session!.GetAll(EntityKind.Player);
            var clubs = vm.Session.GetAll(EntityKind.Club);
            vm.DisconnectCommand.Execute(null);

            // Every other player has a face; every club has a badge; no nation has a flag.
            WritePack(Path.Combine(root, "faces", "any pack name", "faces"), "person", "portrait", "face_",
                players.Take(60).Where((_, i) => i % 2 == 0).Select(p => p.Id));
            WritePack(Path.Combine(root, "logos", "Clubs", "Normal", "Small"), "club", "icon", "", clubs.Select(c => c.Id));

            AppServices.PicturesForDemo = true;
            settings.GraphicsFolder = root;
            settings.ShowPictures = true;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;

            var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
            w.Show();
            vm.OpenDemoCommand.Execute(null);
            Pump();

            var images = ImageService.Instance;
            PumpUntil(() => !images.IsIndexing && !images.Library.IsEmpty);
            Assert.True(images.HasAny(EntityKind.Player));
            Assert.True(images.HasAny(EntityKind.Club));
            Assert.False(images.HasAny(EntityKind.Nation));

            var browser = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
            var withFace = browser.Rows.First(r => r.Entity.Id == players[0].Id);
            var withoutFace = browser.Rows.First(r => r.Entity.Id == players[1].Id);

            // Asking starts the load; the row announces the picture when it arrives.
            _ = withFace.Images[PropertyCatalog.DisplayName];
            _ = withoutFace.Images[PropertyCatalog.DisplayName];
            PumpUntil(() => withFace.Images[PropertyCatalog.DisplayName] is not null);
            Assert.Null(withoutFace.Images[PropertyCatalog.DisplayName]);
            Assert.Null(withFace.Images["Nation"]);

            var grid = w.GetVisualDescendants().OfType<DataGrid>().Single();
            grid.SelectedIndex = browser.Rows.IndexOf(withFace);
            Pump();
            PumpUntil(() => browser.Editor!.Picture.HasImage);
            PumpUntil(() => withFace.Images["Contract.Club"] is not null);
            Shot(w, "players-day-pictures");

            // The row without a face shows no picture element at all in its name cell.
            var avatars = grid.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("avatar")).ToList();
            Assert.Contains(avatars, a => a.IsVisible);
            Assert.Contains(avatars, a => !a.IsVisible);

            // Switching pictures off removes them everywhere.
            vm.Settings.ShowPictures = false;
            Pump();
            Assert.False(images.Active);
            Assert.Null(withFace.Images[PropertyCatalog.DisplayName]);
            Assert.False(browser.Editor!.Picture.HasImage);

            w.Close();
        }
        finally
        {
            AppServices.PicturesForDemo = false;
            settings.GraphicsFolder = null;
            settings.ShowPictures = true;
            ImageService.Instance.Disable();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            Pump();
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}

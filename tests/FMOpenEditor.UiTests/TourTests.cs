using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.Controls;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.UiTests;

/// <summary>Tiki's guided tour: it starts the first time a save is connected, points at real controls, and can be replayed.</summary>
public class TourTests
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
    public void The_tour_waits_for_the_first_connection_and_points_at_controls_that_exist()
    {
        var settings = AppServices.Settings;
        settings.TourWorkspaceSeen = false;
        try
        {
            var vm = new MainWindowViewModel();
            var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
            w.Show();
            Pump();

            // The home screen stays quiet: nothing starts until a save is connected...
            var tour = w.GetVisualDescendants().OfType<TourOverlay>().Single();
            Assert.False(tour.IsRunning);
            Assert.False(settings.TourWorkspaceSeen);

            // ...unless the ? button asks, and that doesn't use up the first-connection tour.
            w.StartTour();
            Assert.True(tour.IsRunning);
            Assert.Equal(MainWindow.HomeTour[0], tour.Current);
            Assert.False(settings.TourWorkspaceSeen);

            // Every stop that names a control must find it on this screen.
            foreach (var step in MainWindow.HomeTour.Where(s => s.Target is not null))
                Assert.Contains(w.GetVisualDescendants().OfType<Control>(), c => c.Name == step.Target && c.IsEffectivelyVisible);

            Pump();
            Shot(w, "tour-home");
            for (var i = 0; i < MainWindow.HomeTour.Count; i++) tour.Next();   // past the last stop: the tour closes
            Pump();
            Assert.False(tour.IsRunning);

            // Connecting to a save for the first time starts the tour, once.
            vm.OpenDemoCommand.Execute(null);
            Pump();
            Assert.True(tour.IsRunning);
            Assert.True(settings.TourWorkspaceSeen);
            Assert.Equal(MainWindow.WorkspaceTour[0], tour.Current);
            foreach (var step in MainWindow.WorkspaceTour.Where(s => s.Target is not null))
                Assert.Contains(w.GetVisualDescendants().OfType<Control>(), c => c.Name == step.Target && c.IsEffectivelyVisible);

            for (var i = 0; i < 4; i++) tour.Next();
            Pump();
            Assert.Equal("TourEditor", tour.Current!.Target);
            Shot(w, "tour-workspace");
            tour.Finish();

            // Seen already: reopening a save doesn't start it again, but the help button does.
            vm.DisconnectCommand.Execute(null);
            vm.OpenDemoCommand.Execute(null);
            Pump();
            Assert.False(tour.IsRunning);
            w.StartTour();
            Assert.True(tour.IsRunning);
            tour.Finish();
            w.Close();
        }
        finally
        {
            settings.TourWorkspaceSeen = true;
        }
    }
}
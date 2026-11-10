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

/// <summary>Tiki's guided tour: it appears once per screen, points at real controls, and can be replayed.</summary>
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
        w.CaptureRenderedFrame()?.Save(Path.Combine(dir!.FullName, "docs", "screenshots", name + ".png"));
    }

    [AvaloniaFact]
    public void Tours_run_once_per_screen_and_point_at_controls_that_exist()
    {
        var settings = AppServices.Settings;
        settings.TourHomeSeen = settings.TourWorkspaceSeen = false;
        try
        {
            var vm = new MainWindowViewModel();
            var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
            w.Show();
            Pump();

            var tour = w.GetVisualDescendants().OfType<TourOverlay>().Single();
            Assert.True(tour.IsRunning);
            Assert.True(settings.TourHomeSeen);
            Assert.Equal(MainWindow.HomeTour[0], tour.Current);

            // Every stop that names a control must find it on this screen.
            foreach (var step in MainWindow.HomeTour.Where(s => s.Target is not null))
                Assert.Contains(w.GetVisualDescendants().OfType<Control>(), c => c.Name == step.Target && c.IsEffectivelyVisible);

            tour.Next();
            Pump();
            Shot(w, "tour-home");
            tour.Next();
            tour.Next();   // past the last stop: the tour closes
            Pump();
            Assert.False(tour.IsRunning);

            // Opening a save starts the workspace tour, once.
            vm.OpenDemoCommand.Execute(null);
            Pump();
            Assert.True(tour.IsRunning);
            Assert.True(settings.TourWorkspaceSeen);
            foreach (var step in MainWindow.WorkspaceTour)
                Assert.Contains(w.GetVisualDescendants().OfType<Control>(), c => c.Name == step.Target && c.IsEffectivelyVisible);

            for (var i = 0; i < 3; i++) tour.Next();
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
            settings.TourHomeSeen = settings.TourWorkspaceSeen = true;
        }
    }
}
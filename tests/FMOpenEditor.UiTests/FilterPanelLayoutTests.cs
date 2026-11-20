using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.UiTests;

public class FilterPanelLayoutTests
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

    /// <summary>A long row ("Age between … and …") fits on one line, even at the window's smallest width.</summary>
    [AvaloniaTheory]
    [InlineData(1620, 880)]
    [InlineData(1920, 1000)]
    public void Long_search_rows_stay_inside_the_panel(int width, int height)
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = width, Height = height };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();
        var browser = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        browser.AddFilterCommand.Execute(null);
        Pump();
        browser.Filters[0].Op = OpOption.All.Single(o => o.Label == "between");
        Pump();

        var panel = w.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "FilterPanel");
        var row = panel.GetVisualDescendants().OfType<WrapPanel>().First();
        foreach (var c in row.Children.Where(c => c.IsVisible))
        {
            var right = c.TranslatePoint(new Point(c.Bounds.Width, 0), panel)!.Value.X;
            Assert.True(right <= panel.Bounds.Width + 0.5, $"{c.GetType().Name} ends at {right}, panel at {panel.Bounds.Width}");
        }
        // At the window's smallest size the whole row is still on one line.
        var first = row.Children[0].Bounds;
        Assert.All(row.Children.Where(c => c.IsVisible), c => Assert.True(c.Bounds.Y < first.Bottom, $"{c.GetType().Name} wrapped to y={c.Bounds.Y}"));
        if (Environment.GetEnvironmentVariable("SHOT") is { } shot) w.CaptureRenderedFrame()?.Save(shot);
        w.Close();
    }
}

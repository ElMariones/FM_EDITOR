using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.UiTests;

public class EditorLayoutTests
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

    /// <summary>
    /// A narrow editor wraps its tabs onto two rows; scrolled to the end, the last field must still
    /// sit inside the editor panel rather than under its bottom edge.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1440, 880)]
    [InlineData(1100, 1000)]
    public void Last_field_is_visible_when_scrolled_to_the_end(int width, int height)
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = width, Height = height };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();
        var browser = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        w.GetVisualDescendants().OfType<DataGrid>().Single().SelectedIndex = 0;
        Pump();
        browser.Editor!.SelectedGroup = browser.Editor.Groups.First();
        Pump();

        var panel = w.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TourEditor");
        var scroll = panel.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.IsEffectivelyVisible);
        scroll.ScrollToEnd();
        Pump();

        var items = scroll.GetVisualDescendants().OfType<ItemsControl>().First(i => i.IsEffectivelyVisible);
        var last = items.ContainerFromIndex(items.ItemCount - 1)!;
        var lastBottom = last.TranslatePoint(new Point(0, last.Bounds.Height), panel)!.Value.Y;
        var scrollBottom = scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), panel)!.Value.Y;
        Assert.True(scrollBottom <= panel.Bounds.Height + 0.5, $"scroll viewer ends at {scrollBottom}, panel at {panel.Bounds.Height}");
        Assert.True(lastBottom <= panel.Bounds.Height + 0.5, $"last field ends at {lastBottom}, panel at {panel.Bounds.Height}");
        w.Close();
    }
}

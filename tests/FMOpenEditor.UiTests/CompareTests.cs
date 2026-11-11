using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.UiTests;

/// <summary>Compare opens a window for two rows of any record type.</summary>
public class CompareTests
{
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Comparing_two_rows_opens_the_window_for_every_record_type()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();

        // Run.Text binds two-way by default; with a StringFormat that wrote back to the row forever
        // and took the app down the moment the window opened.
        var opened = new List<CompareWindow>();
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<CompareWindow>((c, _) => opened.Add(c));
        foreach (var nav in vm.Nav.ToList())
        {
            vm.SelectedNav = nav;
            Pump();
            if (vm.CurrentPage is not BrowserViewModel page || page.Rows.Count < 2) continue;

            opened.Clear();
            page.SetSelection(page.Rows.Take(2));
            page.CompareCommand.Execute(null);
            Pump();

            var window = Assert.Single(opened);
            var compare = Assert.IsType<CompareViewModel>(window.DataContext);
            Assert.NotEmpty(compare.Rows);
            window.Close();
            Pump();
        }
        w.Close();
    }
}

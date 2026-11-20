using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.UiTests;

public class AttributesTabTests
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
    public void Attributes_tab_highlights_the_chosen_roles_attributes()
    {
        var vm = new MainWindowViewModel();
        var w = new MainWindow { DataContext = vm, Width = 1440, Height = 880 };
        w.Show();
        vm.OpenDemoCommand.Execute(null);
        Pump();
        var browser = Assert.IsType<BrowserViewModel>(vm.CurrentPage);
        w.GetVisualDescendants().OfType<DataGrid>().Single().SelectedIndex = 0;
        Pump();

        Assert.Equal(["Overview", "Attributes", "Positions", "Contract", "Hidden & Status"], browser.Editor!.Tabs.Select(t => t.Name).Take(5));
        var roles = Assert.Single(browser.Editor.Tabs.OfType<AttributesTabViewModel>());
        browser.Editor.SelectedGroup = roles;
        Pump();
        roles.SelectedPosition = "D (C)";
        Pump();
        Assert.All(roles.PositionRoles, r => Assert.Equal("D (C)", r.Position));
        Assert.Equal(roles.PositionRoles[0], roles.SelectedRole);
        roles.SelectedRole = roles.PositionRoles.Single(r => r.Name == "Ball-Playing Centre-Back");
        Pump();
        Assert.Equal("D (C)", roles.SelectedPosition);

        var rows = roles.Columns.SelectMany(c => c.Rows).ToList();
        Assert.Equal(["Technical", "Mental", "Physical"], roles.Columns.Select(c => c.Name));
        Assert.Equal(9, rows.Count(r => r.IsKey));
        Assert.Equal(8, rows.Count(r => r.IsPreferred));
        Assert.True(rows.Single(r => r.Editor.Label == "Heading").IsKey);
        Assert.True(rows.Single(r => r.Editor.Label == "Vision").IsPreferred);
        Assert.Contains(w.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("rolekey") && b.IsEffectivelyVisible);

        // Two columns side by side: Mental starts to the right of Technical, on the same row.
        var heads = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text is "Technical" or "Mental" && t.Classes.Contains("muted") && t.IsEffectivelyVisible).ToList();
        var tech = heads.Single(t => t.Text == "Technical").TranslatePoint(default, w)!.Value;
        var mental = heads.Single(t => t.Text == "Mental").TranslatePoint(default, w)!.Value;
        Assert.Equal(tech.Y, mental.Y, 1);
        Assert.True(mental.X > tech.X);

        // Each attribute's spinner fits inside its row: nothing is cut on the right.
        foreach (var row in w.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("rolerow") && b.IsEffectivelyVisible))
        {
            var spin = row.GetVisualDescendants().OfType<NumericUpDown>().Single();
            var right = spin.TranslatePoint(new Point(spin.Bounds.Width, 0), row)!.Value.X;
            Assert.True(right <= row.Bounds.Width + 0.5, $"spinner ends at {right}, row at {row.Bounds.Width}");
        }

        if (Environment.GetEnvironmentVariable("SHOT") is { } shot)
            w.CaptureRenderedFrame()?.Save(shot);
        w.Close();
    }
}

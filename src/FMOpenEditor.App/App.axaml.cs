using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Apply the saved theme before any window exists, so there is no flash of the wrong one.
        var settings = AppServices.Settings;
        AppServices.ApplyTheme(settings.Theme);
        if (settings.ReduceMotion) Motion.Enabled = false;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            if (desktop.Args?.Contains("--demo") == true) vm.OpenDemoCommand.Execute(null);
            desktop.Exit += (_, _) => vm.Dispose();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.App.Views;

namespace FMOpenEditor.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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

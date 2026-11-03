using Avalonia.Controls;
using FMOpenEditor.App.ViewModels;

namespace FMOpenEditor.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.CompareRequested += c => new CompareWindow { DataContext = c }.Show(this);
        };
    }
}

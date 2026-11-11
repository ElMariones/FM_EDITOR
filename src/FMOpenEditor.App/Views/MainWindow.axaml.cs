using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FMOpenEditor.App.Controls;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;

namespace FMOpenEditor.App.Views;

public partial class MainWindow : Window
{
    /// <summary>Shown on first launch, before any save is open.</summary>
    public static readonly IReadOnlyList<TourStep> HomeTour =
    [
        new("Hi, I'm Tiki", "I keep an eye on your save while you edit it. Give me twenty seconds and I'll show you around."),
        new("Start here",
            "Start Football Manager and load your save, then connect. Nothing is written to the game until you save.",
            "TourConnect"),
        new("Make it yours",
            "Switch between day and night, and check your graphics folder so faces and badges show up. The ? brings me back any time.",
            "TourAppearance"),
    ];

    /// <summary>Shown the first time a save (or the demo) is opened.</summary>
    public static readonly IReadOnlyList<TourStep> WorkspaceTour =
    [
        new("Your whole database",
            "Players, staff, clubs, competitions and nations. The watch list keeps frozen attributes in place while you play.",
            "TourSidebar"),
        new("Find anyone",
            "Search by name, or open Advanced search to filter by any field. Mass edit changes every selected row at once.",
            "TourSearch"),
        new("One-click presets", "Select one row or a hundred, then click a preset: Heal, Inspire, Make WonderKid and more.", "TourPresets"),
        new("Pick a row, edit anything",
            "Everything about them opens here, grouped into tabs. Right-click a row to freeze attributes or compare two.",
            "TourEditor"),
        new("Nothing is written until you save",
            "Edits wait here as pending, and I turn warm while they do. Save changes writes them into the game; Undo last save takes them back.",
            "TourSaveBar"),
    ];

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            vm.CompareRequested += c => new CompareWindow { DataContext = c }.Show(this);
            vm.PropertyChanged += OnViewModelChanged;
        };
        Opened += (_, _) => OfferTour();
        // The mouse's back/forward side buttons step through history, wherever the pointer is.
        AddHandler(PointerPressedEvent, OnMouseHistory, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnMouseHistory(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var command = e.GetCurrentPoint(this).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.XButton1Pressed => vm.GoBackCommand,
            PointerUpdateKind.XButton2Pressed => vm.GoForwardCommand,
            _ => null,
        };
        if (command is null) return;
        e.Handled = true;
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var vm = (MainWindowViewModel)sender!;
        switch (e.PropertyName)
        {
            // Each page rises into place as it is shown.
            case nameof(MainWindowViewModel.CurrentPage):
                Motion.Enter(PageHost);
                break;
            // A new status message slides in so it gets noticed.
            case nameof(MainWindowViewModel.Status):
                Motion.Enter(StatusText, offsetY: 6, milliseconds: 240);
                break;
            // The pending pill bumps every time the count changes; Tiki smiles when it reaches zero by saving.
            case nameof(MainWindowViewModel.PendingCount):
                Motion.Pop(PendingPill);
                break;
            case nameof(MainWindowViewModel.CanUndo) when vm.CanUndo && !vm.HasPending:
                TopMascot.Cheer();
                break;
            case nameof(MainWindowViewModel.IsNight):
                Motion.Spin(ThemeIcon);
                Motion.Enter(PageHost, offsetY: 0, milliseconds: 420);
                break;
            case nameof(MainWindowViewModel.IsConnected):
                Tour.Finish();
                OfferTour();
                break;
        }
    }

    /// <summary>Runs whichever tour hasn't been seen yet for the current screen.</summary>
    private void OfferTour()
    {
        var settings = AppServices.Settings;
        var connected = DataContext is MainWindowViewModel { IsConnected: true };
        if (connected ? settings.TourWorkspaceSeen : settings.TourHomeSeen) return;
        // After layout, so the controls the tour points at have their final positions.
        Dispatcher.UIThread.Post(() => StartTour(), DispatcherPriority.Background);
    }

    /// <summary>Starts the tour for the current screen and remembers that it has been shown.</summary>
    public void StartTour()
    {
        if (Tour.IsRunning) return;
        var settings = AppServices.Settings;
        var connected = DataContext is MainWindowViewModel { IsConnected: true };
        if (connected) settings.TourWorkspaceSeen = true;
        else settings.TourHomeSeen = true;
        settings.Save();
        Tour.Start(connected ? WorkspaceTour : HomeTour, () => TopMascot.Cheer());
    }

    private void OnHelp(object? sender, RoutedEventArgs e) => StartTour();
}

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;

namespace FMOpenEditor.App.Views;

public partial class TransferWindow : Window
{
    private TransferViewModel? _vm;

    public TransferWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
            _vm = DataContext as TransferViewModel;
            if (_vm is null) return;
            _vm.CloseRequested += Close;
            _vm.PropertyChanged += OnViewModelChanged;
        };
        Opened += (_, _) => ClubSearchBox.Focus();

        // Keyboard: type, Down into the results, Enter picks; Escape backs out of a second search.
        ClubSearchBox.KeyDown += (_, e) =>
        {
            if (_vm is null) return;
            switch (e.Key)
            {
                case Key.Down when _vm.HasResults:
                    ClubList.SelectedIndex = Math.Min(1, _vm.Results.Count - 1);
                    (ClubList.ContainerFromIndex(ClubList.SelectedIndex) as Control)?.Focus();
                    e.Handled = true;
                    break;
                case Key.Enter when _vm.Highlighted is not null:
                    _vm.ChooseCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.Escape when _vm.CanCancelPicking:
                    _vm.CancelPickingCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        };
        ClubList.KeyDown += (_, e) =>
        {
            if (_vm is null) return;
            if (e.Key == Key.Enter) { _vm.ChooseCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Key.Escape) { ClubSearchBox.Focus(); e.Handled = true; }
        };
        // A click picks; arrow keys only move the highlight.
        ClubList.Tapped += (_, e) =>
        {
            if (_vm is not null && (e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: ClubOption option })
                _vm.ChooseCommand.Execute(option);
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TransferViewModel.IsPicking) || _vm is null) return;
        if (_vm.IsPicking)
        {
            Motion.Enter(PickPanel, offsetY: 6, milliseconds: 200);
            Dispatcher.UIThread.Post(() =>
            {
                ClubSearchBox.Focus();
                ClubSearchBox.SelectAll();
            });
        }
        else
        {
            Motion.Enter(ChosenCard, offsetY: 6, milliseconds: 220);
            Dispatcher.UIThread.Post(() => FeeBox.Focus());
        }
    }
}

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;

namespace FMOpenEditor.App.Views;

public partial class EntityEditorView : UserControl
{
    private EntityEditorViewModel? _vm;

    public EntityEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
            _vm = DataContext as EntityEditorViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(EntityEditorViewModel.SelectedGroup)) return;
        // The fields of the newly chosen tab slide up into place.
        var content = Tabs.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "PART_SelectedContentHost");
        if (content is not null) Motion.Enter(content, offsetY: 10, milliseconds: 240);
    }
}

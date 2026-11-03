using Avalonia.Controls;
using Avalonia.Data;
using FMOpenEditor.App.ViewModels;

namespace FMOpenEditor.App.Views;

public partial class BrowserView : UserControl
{
    public BrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildColumns();
        Grid.SelectionChanged += (_, _) =>
        {
            if (DataContext is BrowserViewModel vm) vm.SetSelection(Grid.SelectedItems.OfType<EntityRow>());
        };
        Grid.Sorting += (_, e) =>
        {
            // Sort ourselves on the raw values; the grid can't sort indexer-bound columns.
            e.Handled = true;
            if (DataContext is BrowserViewModel vm && e.Column.Tag is string key) vm.SortBy(key);
        };
    }

    private void BuildColumns()
    {
        Grid.Columns.Clear();
        if (DataContext is not BrowserViewModel vm) return;
        foreach (var c in vm.Columns)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = c.Header,
                Tag = c.Key,
                // Reflection binding: compiled bindings don't support runtime-built indexer paths.
                Binding = new Binding($"[{c.Key}]"),
                Width = new DataGridLength(c.Width),
            });
        }
    }
}

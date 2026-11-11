using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FMOpenEditor.App.Services;
using FMOpenEditor.App.ViewModels;
using FMOpenEditor.Core.Model;

namespace FMOpenEditor.App.Views;

public partial class BrowserView : UserControl
{
    private BrowserViewModel? _vm;

    public BrowserView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.PropertyChanged -= OnViewModelChanged;
                _vm.RevealRequested -= Reveal;
                _vm.SortChanged -= UpdateHeaders;
            }
            _vm = DataContext as BrowserViewModel;
            if (_vm is not null)
            {
                _vm.PropertyChanged += OnViewModelChanged;
                _vm.RevealRequested += Reveal;
                _vm.SortChanged += UpdateHeaders;
            }
            BuildColumns();
        };
        // Columns make room for pictures once the graphics index is ready (or give it back).
        AttachedToVisualTree += (_, _) => ImageService.Instance.Changed += BuildColumns;
        DetachedFromVisualTree += (_, _) => ImageService.Instance.Changed -= BuildColumns;
        // Below this width the action buttons show icons only (their tooltips still name them).
        TourSearch.SizeChanged += (_, e) => Toolbar.Classes.Set("compact", e.NewSize.Width < 620);
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

    /// <summary>Selects a row opened from elsewhere and scrolls it into view.</summary>
    private void Reveal(EntityRow row)
    {
        Grid.SelectedItem = row;
        Grid.ScrollIntoView(row, null);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BrowserViewModel.Editor):
                Motion.Enter(EditorHost, offsetY: 10, milliseconds: 260);
                break;
            case nameof(BrowserViewModel.ShowFilters) when _vm!.ShowFilters:
                Motion.Enter(FilterPanel, offsetY: -8, milliseconds: 220);
                break;
            case nameof(BrowserViewModel.ShowMassEdit) when _vm!.ShowMassEdit:
                Motion.Enter(MassPanel, offsetY: -8, milliseconds: 220);
                break;
        }
    }

    private void BuildColumns()
    {
        Grid.Columns.Clear();
        if (DataContext is not BrowserViewModel vm) return;
        foreach (var c in vm.Columns)
        {
            var def = PropertyCatalog.Get(vm.Kind, c.Key);
            Grid.Columns.Add(new DataGridTemplateColumn
            {
                Tag = c.Key,
                SortMemberPath = c.Key,
                CanUserSort = true,
                Width = new DataGridLength(c.Width + PictureAllowance(vm.Kind, c.Key, def)),
                MinWidth = 36,
                CellTemplate = new FuncDataTemplate<EntityRow>((_, _) => BuildCell(vm, c.Key, def), supportsRecycling: true),
            });
        }
        UpdateHeaders();
    }

    /// <summary>Header labels, with an arrow on the column the rows are sorted by.</summary>
    private void UpdateHeaders()
    {
        if (DataContext is not BrowserViewModel vm) return;
        foreach (var col in Grid.Columns)
        {
            if (col.Tag is not string key || vm.Columns.FirstOrDefault(c => c.Key == key) is not { } c) continue;
            var label = c.Header.ToUpperInvariant();
            col.Header = key == vm.SortKey ? $"{label} {(vm.SortDescending ? "↓" : "↑")}" : label;
        }
    }

    /// <summary>Which kind of entity a column can show a picture of, if any.</summary>
    private static EntityKind? PictureKind(EntityKind kind, string key, PropertyDef def) =>
        key == PropertyCatalog.DisplayName ? kind
        : def.Kind == ValueKind.Reference ? def.RefKind
        : null;

    /// <summary>Extra width for the picture, only when the user's packs have pictures of that kind.</summary>
    private static double PictureAllowance(EntityKind kind, string key, PropertyDef def)
    {
        const double headerPadding = 0;
        if (PictureKind(kind, key, def) is not { } pictureKind || !ImageService.Instance.HasAny(pictureKind)) return headerPadding;
        return headerPadding + (key == PropertyCatalog.DisplayName ? 42 : 28);
    }

    /// <summary>
    /// A cell is "[picture] text". The picture is bound to the row's image for this column and is
    /// collapsed whenever there isn't one, so a row without a face or badge is simply its name.
    /// Reference cells (club, nation, league) open what they point at on double-click.
    /// </summary>
    private static Control BuildCell(BrowserViewModel vm, string key, PropertyDef def)
    {
        var kind = vm.Kind;
        // Reflection bindings: compiled bindings don't support runtime-built indexer paths.
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Bind(TextBlock.TextProperty, new Binding($"[{key}]"));
        if (def.IsNumeric) text.Classes.Add("num");

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };

        var isName = key == PropertyCatalog.DisplayName;
        if (PictureKind(kind, key, def) is { } pictureKind)
        {
            var person = pictureKind is EntityKind.Player or EntityKind.Staff;
            var size = isName ? 30.0 : 20.0;
            var image = new Image { Stretch = person ? Stretch.UniformToFill : Stretch.Uniform };
            RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
            image.Bind(Image.SourceProperty, new Binding($"Images[{key}]"));

            var avatar = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(person ? size / 2 : 4),
                ClipToBounds = true,
                Margin = new Thickness(0, 0, isName ? 12 : 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = image,
            };
            avatar.Classes.Add("avatar");
            if (person) avatar[!Border.BackgroundProperty] = new DynamicResourceExtension("FieldBrush");
            avatar.Bind(IsVisibleProperty, new Binding($"Images[{key}]") { Converter = ObjectConverters.IsNotNull });
            panel.Children.Add(avatar);
        }

        if (isName) text.FontWeight = FontWeight.SemiBold;
        panel.Children.Add(text);

        if (isName)
        {
            // A warm dot marks rows that carry unsaved changes.
            var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            dot[!Shape.FillProperty] = new DynamicResourceExtension("PendingBrush");
            dot.Bind(IsVisibleProperty, new Binding(nameof(EntityRow.HasPending)));
            ToolTip.SetTip(dot, "Unsaved changes");
            panel.Children.Add(dot);
        }

        if (def.Kind != ValueKind.Reference || def.RefKind is not { } refKind) return panel;

        // The whole cell is the target, not just the text, so it needs a hit-testable background.
        text.Classes.Add("link");
        var cell = new Border { Background = Brushes.Transparent, Child = panel, Cursor = new Cursor(StandardCursorType.Hand) };
        cell.Classes.Add("refcell");
        ToolTip.SetTip(cell, $"Double-click to open this {refKind switch { EntityKind.Competition => "competition", EntityKind.Nation => "nation", _ => "club" }}");
        ToolTip.SetShowDelay(cell, 700);
        cell.DoubleTapped += (_, e) =>
        {
            if (cell.DataContext is not EntityRow row) return;
            e.Handled = true;
            vm.OpenReference(row, key);
        };
        return cell;
    }
}

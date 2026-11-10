using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
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
            if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
            _vm = DataContext as BrowserViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
            BuildColumns();
        };
        // Columns make room for pictures once the graphics index is ready (or give it back).
        AttachedToVisualTree += (_, _) => ImageService.Instance.Changed += BuildColumns;
        DetachedFromVisualTree += (_, _) => ImageService.Instance.Changed -= BuildColumns;
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
                Header = c.Header.ToUpperInvariant(),
                Tag = c.Key,
                SortMemberPath = c.Key,
                CanUserSort = true,
                Width = new DataGridLength(c.Width + PictureAllowance(vm.Kind, c.Key, def)),
                CellTemplate = new FuncDataTemplate<EntityRow>((_, _) => BuildCell(vm.Kind, c.Key, def), supportsRecycling: true),
            });
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
        const double headerPadding = 10;
        if (PictureKind(kind, key, def) is not { } pictureKind || !ImageService.Instance.HasAny(pictureKind)) return headerPadding;
        return headerPadding + (key == PropertyCatalog.DisplayName ? 42 : 28);
    }

    /// <summary>
    /// A cell is "[picture] text". The picture is bound to the row's image for this column and is
    /// collapsed whenever there isn't one, so a row without a face or badge is simply its name.
    /// </summary>
    private static Control BuildCell(EntityKind kind, string key, PropertyDef def)
    {
        // Reflection bindings: compiled bindings don't support runtime-built indexer paths.
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Bind(TextBlock.TextProperty, new Binding($"[{key}]"));
        if (def.IsNumeric) text.Classes.Add("num");

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center };

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

        return panel;
    }
}

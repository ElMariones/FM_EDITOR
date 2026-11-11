using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace FMOpenEditor.App.Controls;

/// <summary>
/// The app's own line-icon set: 24-unit grid, 1.75 stroke, round caps and joins, drawn in the
/// inherited text colour so an icon always matches the label beside it.
/// </summary>
public sealed class Icon : Control
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = "M3 11 L12 3 L21 11 M5.5 9.5 V20 H10 V14 H14 V20 H18.5 V9.5",
        ["player"] = "M12 12 A4 4 0 1 0 12 4 A4 4 0 0 0 12 12 Z M4 21 C4 16.5 7.5 15 12 15 C16.5 15 20 16.5 20 21",
        ["staff"] = "M9 3.5 H15 V7 H9 Z M9 5 H6 V21 H18 V5 H15 M9 12 H15 M9 16 H13",
        ["club"] = "M12 3 L20 6 V11.5 C20 16.5 16.5 19.5 12 21 C7.5 19.5 4 16.5 4 11.5 V6 Z M4.3 10.5 H19.7 M12 3 V21",
        ["trophy"] = "M8 4 H16 V10 A4 4 0 0 1 8 10 Z M8 6 H5 V7.5 A3 3 0 0 0 8 10.5 M16 6 H19 V7.5 A3 3 0 0 1 16 10.5 M12 14 V18 M9.5 18 H14.5 M8.5 20.5 H15.5",
        ["globe"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 Z M3 12 H21 M12 3 C8.5 6.5 8.5 17.5 12 21 M12 3 C15.5 6.5 15.5 17.5 12 21",
        ["snow"] = "M12 2 V22 M3.3 7 L20.7 17 M3.3 17 L20.7 7 M9.5 3.5 L12 6 L14.5 3.5 M9.5 20.5 L12 18 L14.5 20.5",
        ["flask"] = "M9 3 H15 M10 3 V9 L4.5 19 A1.4 1.4 0 0 0 5.8 21 H18.2 A1.4 1.4 0 0 0 19.5 19 L14 9 V3 M7.5 15 H16.5",
        ["settings"] = "M4 7 H12 M16 7 H20 M14 5 V9 M4 17 H8 M12 17 H20 M10 15 V19",
        ["edit"] = "M4 20 L8.5 19 L19.5 8 L16 4.5 L5 15.5 Z M14 6.5 L17.5 10",
        ["sun"] = "M12 8 A4 4 0 1 0 12 16 A4 4 0 0 0 12 8 Z M12 2 V4 M12 20 V22 M2 12 H4 M20 12 H22 M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M4.9 19.1 L6.3 17.7 M17.7 6.3 L19.1 4.9",
        ["moon"] = "M20 14.5 A8.5 8.5 0 1 1 9.5 4 A7 7 0 0 0 20 14.5 Z",
        ["refresh"] = "M20 11 A8 8 0 0 0 5.5 7.5 L4 9 M4 4 V9 H9 M4 13 A8 8 0 0 0 18.5 16.5 L20 15 M20 20 V15 H15",
        ["power"] = "M12 3 V11 M7 6.2 A8 8 0 1 0 17 6.2",
        ["search"] = "M11 4 A7 7 0 1 0 11 18 A7 7 0 0 0 11 4 Z M16 16 L21 21",
        ["filter"] = "M3 5 H21 L14 13 V20 L10 18 V13 Z",
        ["compare"] = "M4 5 H10 V19 H4 Z M14 5 H20 V19 H14 Z",
        ["save"] = "M5 4 H16 L20 8 V20 H5 Z M8 4 V9 H15 V4 M8 20 V14 H16 V20",
        ["undo"] = "M8 5 L3 10 L8 15 M3 10 H14.5 A5.5 5.5 0 0 1 14.5 21 H10",
        ["discard"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 Z M9 9 L15 15 M15 9 L9 15",
        ["close"] = "M6 6 L18 18 M18 6 L6 18",
        ["plus"] = "M12 5 V19 M5 12 H19",
        ["bolt"] = "M13 2 L4 14 H11 L10 22 L20 9 H13 Z",
        ["image"] = "M4 5 H20 V19 H4 Z M4 16 L9 11 L13 15 L16 12 L20 16 M15 9 A0.6 0.6 0 1 0 15 9.01",
        ["folder"] = "M3 6 H9 L11 8 H21 V19 H3 Z",
        ["chevron"] = "M6 9 L12 15 L18 9",
        ["check"] = "M5 12.5 L10 17.5 L19 7.5",
        ["up"] = "M12 19 V5 M6 11 L12 5 L18 11",
        ["open"] = "M14 4 H20 V10 M20 4 L11 13 M18 14 V20 H4 V6 H10",
        ["back"] = "M15 5 L8 12 L15 19",
        ["forward"] = "M9 5 L16 12 L9 19",
        ["link"] = "M9 15 L15 9 M11 6.5 L13 4.5 A4.2 4.2 0 0 1 19.5 11 L17.5 13 M13 17.5 L11 19.5 A4.2 4.2 0 0 1 4.5 13 L6.5 11",
        ["play"] = "M7 4 L20 12 L7 20 Z",
        ["sparkle"] = "M12 3 L13.8 9.2 L20 11 L13.8 12.8 L12 19 L10.2 12.8 L4 11 L10.2 9.2 Z",
        ["help"] = "M12 3 A9 9 0 1 0 12 21 A9 9 0 0 0 12 3 Z M9.4 9.6 A2.7 2.7 0 1 1 12 12.4 V13.8 M12 16.8 V17",
        ["dot"] = "M12 11 A1 1 0 1 0 12 13 A1 1 0 0 0 12 11 Z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<Icon, string?>(nameof(Kind));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Size), 18);
    public static readonly StyledProperty<double> StrokeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Stroke), 1.75);

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, StrokeProperty, TextElement.ForegroundProperty);
        AffectsMeasure<Icon>(SizeProperty);
    }

    /// <summary>Name of the icon, e.g. "search".</summary>
    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Edge length in device-independent pixels.</summary>
    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Kind is null || !Paths.TryGetValue(Kind, out var data)) return;
        if (!Cache.TryGetValue(Kind, out var geometry)) Cache[Kind] = geometry = Geometry.Parse(data);

        var brush = GetValue(TextElement.ForegroundProperty) ?? Brushes.Gray;
        var pen = new Pen(brush, Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            context.DrawGeometry(null, pen, geometry);
    }
}

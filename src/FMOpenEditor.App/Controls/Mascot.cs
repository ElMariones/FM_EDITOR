using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using FMOpenEditor.App.Services;

namespace FMOpenEditor.App.Controls;

/// <summary>
/// Tiki, the FM OpenEditor bot (named for tiki-taka): a glossy ball with two pill eyes. The ball
/// is the football; the eyes are the editor watching your save.
/// <para>
/// Its skin is the Chroma gradient and turns the warm Sunset gradient while there are unsaved
/// changes. The eyes follow the pointer (or a point the tutorial asks it to look at), blink, and
/// squint into a smile when something goes well.
/// </para>
/// Drawn in code on a 256-unit grid, so the same artwork serves the window icon, the top bar, the
/// home page and the tutorial.
/// </summary>
public sealed class Mascot : Control
{
    private const double Grid = 256;
    private const double Radius = 104;
    private static readonly Point Centre = new(128, 128);

    public static readonly StyledProperty<bool> BadgeProperty = AvaloniaProperty.Register<Mascot, bool>(nameof(Badge));
    public static readonly StyledProperty<double> WarmthProperty = AvaloniaProperty.Register<Mascot, double>(nameof(Warmth));
    public static readonly StyledProperty<bool> FollowPointerProperty = AvaloniaProperty.Register<Mascot, bool>(nameof(FollowPointer), true);
    public static readonly StyledProperty<bool> IsWarmProperty = AvaloniaProperty.Register<Mascot, bool>(nameof(IsWarm));
    public static readonly StyledProperty<Point?> GazeProperty = AvaloniaProperty.Register<Mascot, Point?>(nameof(Gaze));

    private readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromSeconds(3.4) };
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private TopLevel? _top;
    private Vector _look;       // where the eyes are, -1..1 on each axis
    private Vector _target;     // where they are heading
    private double _blinkPhase = 1; // 0..1 through one blink; 1 = not blinking
    private double _cheer;      // 1 = smiling, decays to 0

    static Mascot()
    {
        AffectsRender<Mascot>(BadgeProperty, WarmthProperty);
        IsWarmProperty.Changed.AddClassHandler<Mascot>((m, e) => m.Warmth = e.GetNewValue<bool>() ? 1 : 0);
        GazeProperty.Changed.AddClassHandler<Mascot>((m, _) => m.OnGazeChanged());
    }

    public Mascot()
    {
        // Colour changes glide rather than snap.
        if (Motion.Enabled)
            Transitions = [new DoubleTransition { Property = WarmthProperty, Duration = TimeSpan.FromMilliseconds(700) }];
        _blinkTimer.Tick += (_, _) => Blink();
        _frames.Tick += (_, _) => Step();
    }

    /// <summary>Draws the rounded dark badge behind the bot; this is the app icon.</summary>
    public bool Badge
    {
        get => GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    /// <summary>0 = Chroma (all saved), 1 = Sunset (unsaved changes). Animated.</summary>
    public double Warmth
    {
        get => GetValue(WarmthProperty);
        set => SetValue(WarmthProperty, value);
    }

    /// <summary>Bindable switch for <see cref="Warmth"/>.</summary>
    public bool IsWarm
    {
        get => GetValue(IsWarmProperty);
        set => SetValue(IsWarmProperty, value);
    }

    /// <summary>Whether the eyes track the pointer across the window.</summary>
    public bool FollowPointer
    {
        get => GetValue(FollowPointerProperty);
        set => SetValue(FollowPointerProperty, value);
    }

    /// <summary>A point in window coordinates to look at instead of the pointer (the tutorial uses this).</summary>
    public Point? Gaze
    {
        get => GetValue(GazeProperty);
        set => SetValue(GazeProperty, value);
    }

    /// <summary>Closes and reopens the eyes once.</summary>
    public void Blink()
    {
        if (!Motion.Enabled) return;
        _blinkPhase = 0;
        _frames.Start();
    }

    /// <summary>Squints into a smile for a moment: something went well (a save, a finished tour).</summary>
    public void Cheer()
    {
        if (!Motion.Enabled) return;
        _cheer = 1;
        _frames.Start();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        OnGazeChanged();
        if (!Motion.Enabled) return;
        _blinkTimer.Start();
        _top?.AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _blinkTimer.Stop();
        _frames.Stop();
        _top?.RemoveHandler(PointerMovedEvent, OnPointerMoved);
        _top = null;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!FollowPointer || Gaze is not null) return;
        LookTowards(e.GetPosition(this));
    }

    private void OnGazeChanged()
    {
        if (Gaze is not { } point || _top is null) return;
        if (_top.TranslatePoint(point, this) is { } local) LookTowards(local);
    }

    /// <summary>Aims the eyes at a point given in this control's coordinates.</summary>
    private void LookTowards(Point local)
    {
        var scale = Math.Min(Bounds.Width, Bounds.Height) / Grid;
        if (scale <= 0) return;
        var delta = new Vector(local.X / scale - Centre.X, local.Y / scale - Centre.Y);
        var length = delta.Length;
        // The eyes saturate quickly, so it reads as "looking at" from anywhere in the window.
        _target = length < 0.001 ? default : delta / length * Math.Min(1, length / 220);
        if (Motion.Enabled) _frames.Start();
        else { _look = _target; InvalidateVisual(); }
    }

    /// <summary>One animation frame: ease the eyes towards their target, advance blink and smile.</summary>
    private void Step()
    {
        var moving = false;

        var gap = _target - _look;
        if (gap.Length > 0.004) { _look += gap * 0.2; moving = true; }
        else _look = _target;

        if (_blinkPhase < 1) { _blinkPhase = Math.Min(1, _blinkPhase + 0.085); moving = true; }
        if (_cheer > 0) { _cheer = Math.Max(0, _cheer - 0.012); moving = true; }

        if (!moving) _frames.Stop();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = Math.Min(
            double.IsInfinity(availableSize.Width) ? Grid : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? Grid : availableSize.Height);
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;
        var scale = side / Grid;
        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2));

        if (Badge) DrawBadge(context);

        // With a badge the ball sits inside a margin, as icon guidelines expect.
        using var inner = context.PushTransform(Badge
            ? Matrix.CreateTranslation(-Centre.X, -Centre.Y) * Matrix.CreateScale(0.74, 0.74) * Matrix.CreateTranslation(Centre.X, Centre.Y)
            : Matrix.Identity);

        var warmth = Math.Clamp(Warmth, 0, 1);

        // The ball: lit from the top-left, falling away to a deep edge.
        context.DrawEllipse(Skin(warmth), null, Centre, Radius, Radius);

        // A soft specular highlight sells the gloss.
        var gloss = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1) },
        };
        context.DrawEllipse(gloss, null, new Point(88, 76), 52, 40);

        DrawEyes(context);
    }

    private void DrawEyes(DrawingContext context)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x05, 0x09, 0x14));

        // The face slides across the ball as it looks around, and tips a little into the turn.
        var face = Centre + new Vector(_look.X * 34, _look.Y * 28 + 2);
        var tilt = Matrix.CreateRotation((-7 + _look.X * 6) * Math.PI / 180);
        using var _ = context.PushTransform(tilt * Matrix.CreateTranslation(face.X, face.Y));

        var blink = _blinkPhase < 1 ? Math.Sin(_blinkPhase * Math.PI) : 0;
        var smile = Math.Clamp(_cheer * 4, 0, 1);

        foreach (var x in (double[])[-27, 27])
        {
            if (smile > 0.5)
            {
                // Happy: each eye becomes a little arch.
                var arch = new StreamGeometry();
                using (var g = arch.Open())
                {
                    g.BeginFigure(new Point(x - 13, 8), false);
                    g.ArcTo(new Point(x + 13, 8), new Size(13, 15), 0, false, SweepDirection.Clockwise);
                    g.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(ink, 10, lineCap: PenLineCap.Round), arch);
                continue;
            }

            // Pill eyes; a blink squashes them towards their middle.
            var half = Math.Max(3, 23 * (1 - blink * 0.9));
            context.DrawRectangle(ink, null, new Rect(x - 10.5, -half, 21, half * 2), 10.5, Math.Min(10.5, half));
        }
    }

    private static void DrawBadge(DrawingContext context)
    {
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb(0x1B, 0x25, 0x4F), 0), new GradientStop(Color.FromRgb(0x07, 0x0A, 0x18), 1) },
        };
        var rect = new Rect(4, 4, Grid - 8, Grid - 8);
        context.DrawRectangle(fill, null, rect, 58, 58);

        // A pool of light behind the ball, like a floodlight.
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0x59, 0x2C, 0xE8, 0xB5), 0), new GradientStop(Color.FromArgb(0x00, 0x2C, 0xE8, 0xB5), 1) },
        };
        context.DrawRectangle(glow, null, rect, 58, 58);

        var edge = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF), 0), new GradientStop(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), 1) },
        };
        context.DrawRectangle(null, new Pen(edge, 3), rect, 58, 58);
    }

    /// <summary>The ball's gradient, blended between Chroma and Sunset.</summary>
    private static RadialGradientBrush Skin(double warmth) => new()
    {
        Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.3, 0.24, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.78, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.78, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Mix(Color.FromRgb(0xE6, 0xFF, 0xB0), Color.FromRgb(0xFF, 0xEE, 0xB0), warmth), 0),
            new GradientStop(Mix(Color.FromRgb(0xB4, 0xF8, 0x4E), Color.FromRgb(0xFF, 0xC5, 0x3D), warmth), 0.22),
            new GradientStop(Mix(Color.FromRgb(0x2C, 0xE8, 0xB5), Color.FromRgb(0xFF, 0x7A, 0x45), warmth), 0.55),
            new GradientStop(Mix(Color.FromRgb(0x2F, 0x7B, 0xFF), Color.FromRgb(0xF0, 0x2F, 0x82), warmth), 0.86),
            new GradientStop(Mix(Color.FromRgb(0x1B, 0x3F, 0xB0), Color.FromRgb(0x9E, 0x15, 0x55), warmth), 1),
        },
    };

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}

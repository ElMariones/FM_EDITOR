using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FMOpenEditor.App.Services;

namespace FMOpenEditor.App.Controls;

/// <summary>One stop on a tour: what to say, and the name of the control to point at (null = centre stage).</summary>
public sealed record TourStep(string Title, string Text, string? Target = null);

public partial class TourOverlay : UserControl
{
    private const double Gap = 18;      // between the highlighted control and the bubble
    private const double Inflate = 8;   // how far the ring sits outside the control

    private IReadOnlyList<TourStep> _steps = [];
    private Action? _finished;
    private int _index;

    public TourOverlay()
    {
        InitializeComponent();
        if (!Motion.Enabled)
        {
            // Reduce motion: the ring and bubble jump to each stop instead of gliding.
            Ring.Transitions = null;
            Bubble.Transitions = null;
        }
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
    }

    public bool IsRunning => IsVisible;
    public int StepIndex => _index;
    public TourStep? Current => IsVisible && _index < _steps.Count ? _steps[_index] : null;

    /// <summary>Starts a tour. <paramref name="finished"/> runs when it ends, however it ends.</summary>
    public void Start(IReadOnlyList<TourStep> steps, Action? finished = null)
    {
        if (steps.Count == 0) return;
        _steps = steps;
        _finished = finished;
        _index = 0;
        IsVisible = true;
        Focus();
        Show();
        Motion.Enter(Bubble, offsetY: 16, milliseconds: 360);
    }

    public void Next()
    {
        if (_index >= _steps.Count - 1) { Finish(); return; }
        _index++;
        Show();
    }

    public void Back()
    {
        if (_index == 0) return;
        _index--;
        Show();
    }

    public void Finish()
    {
        if (!IsVisible) return;
        IsVisible = false;
        var done = _finished;
        _finished = null;
        done?.Invoke();
    }

    private void OnNext(object? sender, RoutedEventArgs e) => Next();
    private void OnBack(object? sender, RoutedEventArgs e) => Back();
    private void OnSkip(object? sender, RoutedEventArgs e) => Finish();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape: Finish(); break;
            case Key.Right or Key.Enter or Key.Space: Next(); break;
            case Key.Left: Back(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Show()
    {
        var step = _steps[_index];
        var last = _index == _steps.Count - 1;
        StepLabel.Text = $"{_index + 1} OF {_steps.Count}";
        TitleText.Text = step.Title;
        BodyText.Text = step.Text;
        BackButton.IsVisible = _index > 0;
        SkipButton.IsVisible = !last;
        NextButton.Content = last ? "Got it" : "Next";
        if (last) Guide.Cheer();
        else Guide.Blink();

        Place();
        // The target may not be laid out yet (a page that has only just appeared).
        Dispatcher.UIThread.Post(() => { if (IsVisible) Place(); }, DispatcherPriority.Background);
    }

    private Control? FindTarget(string? name)
    {
        if (name is null || TopLevel.GetTopLevel(this) is not { } top) return null;
        return top.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => c.Name == name && c.IsEffectivelyVisible && c.Bounds.Width > 0);
    }

    /// <summary>Puts the hole, the ring and the bubble where the current step needs them.</summary>
    private void Place()
    {
        if (_index >= _steps.Count) return;
        var area = new Rect(Bounds.Size);
        if (area.Width <= 0 || area.Height <= 0) return;

        Rect? hole = null;
        if (FindTarget(_steps[_index].Target) is { } target && target.TranslatePoint(default, this) is { } origin)
            hole = new Rect(origin, target.Bounds.Size).Inflate(Inflate).Intersect(area);

        var full = new RectangleGeometry(area);
        Dim.Data = hole is { } h
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(h) { RadiusX = 18, RadiusY = 18 })
            : full;

        if (hole is { } ring)
        {
            Ring.Margin = new Thickness(ring.X, ring.Y, 0, 0);
            Ring.Width = ring.Width;
            Ring.Height = ring.Height;
            Ring.Opacity = 1;
        }
        else
        {
            Ring.Opacity = 0;
        }

        Bubble.Measure(new Size(Bubble.Width, double.PositiveInfinity));
        var size = new Size(Bubble.Width, Bubble.DesiredSize.Height);
        var spot = BubblePosition(area, hole, size);
        Bubble.Margin = new Thickness(spot.X, spot.Y, 0, 0);

        // Tiki looks at what it is describing.
        Guide.Gaze = hole is { } look && TopLevel.GetTopLevel(this) is { } top
            ? this.TranslatePoint(look.Center, top)
            : null;
    }

    /// <summary>Beside the target where there is room (right, left, below, above), otherwise centred.</summary>
    private static Point BubblePosition(Rect area, Rect? hole, Size size)
    {
        var centre = new Point((area.Width - size.Width) / 2, (area.Height - size.Height) / 2);
        if (hole is not { } h) return centre;

        double ClampX(double x) => Math.Clamp(x, Gap, Math.Max(Gap, area.Width - size.Width - Gap));
        double ClampY(double y) => Math.Clamp(y, Gap, Math.Max(Gap, area.Height - size.Height - Gap));
        var alignedY = ClampY(h.Center.Y - size.Height / 2);
        var alignedX = ClampX(h.Center.X - size.Width / 2);

        if (h.Right + Gap + size.Width <= area.Width - Gap) return new Point(h.Right + Gap, alignedY);
        if (h.X - Gap - size.Width >= Gap) return new Point(h.X - Gap - size.Width, alignedY);
        if (h.Bottom + Gap + size.Height <= area.Height - Gap) return new Point(alignedX, h.Bottom + Gap);
        if (h.Y - Gap - size.Height >= Gap) return new Point(alignedX, h.Y - Gap - size.Height);
        return centre;
    }
}

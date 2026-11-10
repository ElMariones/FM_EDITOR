using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace FMOpenEditor.App.Services;

/// <summary>
/// Decorative motion, switchable in one place. Hover and press transitions live in the styles and
/// always run; this covers entrances and looping animation, which Reduce motion and the headless
/// UI tests turn off.
/// </summary>
public static class Motion
{
    public static bool Enabled { get; set; } = true;

    /// <summary>Raised when <see cref="Enabled"/> is changed through <see cref="Set"/>.</summary>
    public static event Action? Changed;

    public static void Set(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        Loops.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in Loops)
            if (w.TryGetTarget(out var control) && GetLoop(control) is { } name) control.Classes.Set(name, enabled);
        Changed?.Invoke();
    }

    private static readonly List<WeakReference<Control>> Loops = [];

    /// <summary>
    /// Names a style class that starts a looping animation (see Controls.axaml). The class is only
    /// applied while motion is enabled, so "Reduce motion" stops every loop at once.
    /// </summary>
    public static readonly AttachedProperty<string?> LoopProperty =
        AvaloniaProperty.RegisterAttached<Control, string?>("Loop", typeof(Motion));

    public static string? GetLoop(Control control) => control.GetValue(LoopProperty);
    public static void SetLoop(Control control, string? value) => control.SetValue(LoopProperty, value);

    static Motion()
    {
        LoopProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.OldValue is string old) control.Classes.Remove(old);
            if (e.NewValue is not string name) return;
            Loops.Add(new WeakReference<Control>(control));
            control.Classes.Set(name, Enabled);
        });
    }

    /// <summary>Fades and lifts a control into place. Used when a page or panel appears.</summary>
    public static void Enter(Control control, double offsetY = 14, int milliseconds = 320, int delay = 0)
    {
        if (!Enabled) return;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            Delay = TimeSpan.FromMilliseconds(delay),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Backward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.YProperty, offsetY) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.YProperty, 0.0) },
                },
            },
        };
        _ = animation.RunAsync(control);
    }

    /// <summary>One full turn, used when an icon swaps for another (sun to moon).</summary>
    public static void Spin(Control control)
    {
        if (!Enabled) return;
        control.RenderTransformOrigin = RelativePoint.Center;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(520),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(RotateTransform.AngleProperty, -180.0), new Setter(Visual.OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(RotateTransform.AngleProperty, 0.0), new Setter(Visual.OpacityProperty, 1.0) } },
            },
        };
        _ = animation.RunAsync(control);
    }

    /// <summary>A quick scale bump that draws the eye to something that just changed.</summary>
    public static void Pop(Control control)
    {
        if (!Enabled) return;
        control.RenderTransformOrigin = RelativePoint.Center;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(380),
            Easing = new BackEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(ScaleTransform.ScaleXProperty, 0.7), new Setter(ScaleTransform.ScaleYProperty, 0.7) },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) },
                },
            },
        };
        _ = animation.RunAsync(control);
    }
}

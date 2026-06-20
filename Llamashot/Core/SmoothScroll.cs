using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Llamashot.Core;

/// <summary>
/// Attached behaviour that turns a <see cref="ScrollViewer"/>'s jumpy default mouse-wheel
/// scrolling into a slower, eased, animated glide that always reaches the very bottom.
/// Enable with <c>core:SmoothScroll.Enabled="True"</c> on a ScrollViewer.
/// </summary>
public static class SmoothScroll
{
    // Pixels travelled per wheel notch (one notch == ±120 delta). Lower == calmer scroll.
    private const double PixelsPerNotch = 64.0;
    private static readonly Duration GlideDuration = new(TimeSpan.FromMilliseconds(240));

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    // Where the in-flight glide is heading (NaN == no glide is owning the offset right now).
    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(double.NaN));

    // Animatable proxy: setting it scrolls the viewer (ScrollViewer.VerticalOffset is read-only).
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0.0, OnOffsetChanged));

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv) sv.ScrollToVerticalOffset((double)e.NewValue);
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;
        if ((bool)e.NewValue) sv.PreviewMouseWheel += OnWheel;
        else sv.PreviewMouseWheel -= OnWheel;
    }

    public static void Attach(ScrollViewer sv) => SetEnabled(sv, true);

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = (ScrollViewer)sender;
        if (sv.ScrollableHeight <= 0) return; // nothing to scroll — let it bubble to a parent
        e.Handled = true;

        double target = (double)sv.GetValue(TargetProperty);
        // If no glide is active, or the offset has drifted (drag, keyboard, focus), re-anchor.
        if (double.IsNaN(target) || System.Math.Abs(target - sv.VerticalOffset) > sv.ViewportHeight)
            target = sv.VerticalOffset;

        double step = -e.Delta / 120.0 * PixelsPerNotch;
        target = System.Math.Max(0, System.Math.Min(sv.ScrollableHeight, target + step));
        sv.SetValue(TargetProperty, target);

        sv.SetValue(OffsetProperty, sv.VerticalOffset);
        var anim = new DoubleAnimation(target, GlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => sv.SetValue(TargetProperty, double.NaN);
        sv.BeginAnimation(OffsetProperty, anim);
    }
}

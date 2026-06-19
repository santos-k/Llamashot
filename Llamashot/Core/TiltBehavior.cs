using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Llamashot.Core;

/// <summary>
/// Attached behaviour that leans an element toward the cursor (a subtle 3-D hover) with a
/// small lift/scale. Implemented with a skew + scale transform group (no PlaneProjection,
/// which isn't available in this WPF build). Enable with <c>core:Tilt.Enabled="True"</c> on
/// any element, or <c>Tilt.Attach(border)</c> from code-behind. Honours AppSettings.Enable3DTilt.
/// </summary>
public static class Tilt
{
    private const double MaxSkew = 4.0;     // peak skew (deg) at the card edges
    private const double HoverScale = 1.03;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(Tilt), new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    /// <summary>Convenience for code-behind-generated cards.</summary>
    public static void Attach(FrameworkElement fe) => SetEnabled(fe, true);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            fe.MouseEnter += OnEnter;
            fe.MouseMove += OnMove;
            fe.MouseLeave += OnLeave;
        }
        else
        {
            fe.MouseEnter -= OnEnter;
            fe.MouseMove -= OnMove;
            fe.MouseLeave -= OnLeave;
        }
    }

    private static (ScaleTransform scale, SkewTransform skew) Ensure(FrameworkElement fe)
    {
        if (fe.RenderTransform is TransformGroup g && g.Children.Count == 2
            && g.Children[0] is ScaleTransform s0 && g.Children[1] is SkewTransform sk0)
            return (s0, sk0);

        var scale = new ScaleTransform(1, 1);
        var skew = new SkewTransform(0, 0);
        var group = new TransformGroup();
        group.Children.Add(scale);
        group.Children.Add(skew);
        fe.RenderTransform = group;
        fe.RenderTransformOrigin = new Point(0.5, 0.5);
        return (scale, skew);
    }

    private static void OnEnter(object sender, MouseEventArgs e)
    {
        if (!AppSettings.Instance.Enable3DTilt) return;
        var (scale, skew) = Ensure((FrameworkElement)sender);
        // Release any in-flight leave animations so direct value sets in OnMove take effect.
        skew.BeginAnimation(SkewTransform.AngleXProperty, null);
        skew.BeginAnimation(SkewTransform.AngleYProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (!AppSettings.Instance.Enable3DTilt) return;
        var fe = (FrameworkElement)sender;
        if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return;

        var (scale, skew) = Ensure(fe);
        var p = e.GetPosition(fe);
        double fx = p.X / fe.ActualWidth - 0.5;
        double fy = p.Y / fe.ActualHeight - 0.5;

        // Horizontal cursor → vertical skew, vertical cursor → horizontal skew → reads as a lean.
        skew.AngleY = fx * MaxSkew;
        skew.AngleX = -fy * MaxSkew;
        scale.ScaleX = scale.ScaleY = HoverScale;
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        if (fe.RenderTransform is not TransformGroup g || g.Children.Count != 2) return;
        var scale = (ScaleTransform)g.Children[0];
        var skew = (SkewTransform)g.Children[1];

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(200);
        skew.BeginAnimation(SkewTransform.AngleXProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
        skew.BeginAnimation(SkewTransform.AngleYProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
    }
}

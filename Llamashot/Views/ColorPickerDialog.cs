using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
// disambiguate the few WinForms-ambiguous names not already covered by GlobalUsings.cs
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace Llamashot.Views;

/// <summary>A compact, modern dark-themed color picker (HSV square + hue slider + hex/RGB).</summary>
public class ColorPickerDialog : Window
{
    private double _h, _s = 1, _v = 1;     // hue 0..360, sat/val 0..1
    private bool _sync;                      // guards re-entrant updates

    private readonly Rectangle _svHue;
    private readonly Border _svBox;
    private readonly Ellipse _svThumb;
    private readonly Canvas _svThumbLayer;
    private readonly Border _hueBox;
    private readonly Border _hueThumb;
    private readonly Canvas _hueThumbLayer;
    private readonly TextBox _hexBox;
    private readonly TextBox _rBox, _gBox, _bBox;
    private readonly Border _preview;

    private const double SvW = 268, SvH = 196, HueW = 22;

    public string? ResultHex { get; private set; }

    public ColorPickerDialog(string initialHex)
    {
        Title = "Pick a color";
        Width = 360; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = Res("HeaderBrush", "#1B1F2B");
        Foreground = Res("TextPrimaryBrush", "#E6E8EF");

        var root = new StackPanel { Margin = new Thickness(16) };

        // --- SV square + hue slider row ---
        var row = new StackPanel { Orientation = Orientation.Horizontal };

        _svHue = new Rectangle();
        var whiteRect = new Rectangle
        {
            Fill = new LinearGradientBrush(Colors.White, System.Windows.Media.Color.FromArgb(0, 255, 255, 255), 0) // left→right
        };
        var blackRect = new Rectangle
        {
            Fill = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), Colors.Black, 90)       // top→bottom
        };
        _svThumb = new Ellipse { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
        _svThumb.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black };
        _svThumbLayer = new Canvas { IsHitTestVisible = false };
        _svThumbLayer.Children.Add(_svThumb);
        var svGrid = new Grid();
        svGrid.Children.Add(_svHue); svGrid.Children.Add(whiteRect); svGrid.Children.Add(blackRect); svGrid.Children.Add(_svThumbLayer);
        _svBox = new Border { Width = SvW, Height = SvH, CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = svGrid, Cursor = Cursors.Cross };
        _svBox.MouseLeftButtonDown += (_, e) => { _svBox.CaptureMouse(); PickSV(e.GetPosition(_svBox)); };
        _svBox.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) PickSV(e.GetPosition(_svBox)); };
        _svBox.MouseLeftButtonUp += (_, _) => _svBox.ReleaseMouseCapture();
        row.Children.Add(_svBox);

        _hueBox = new Border
        {
            Width = HueW, Height = SvH, Margin = new Thickness(12, 0, 0, 0), CornerRadius = new CornerRadius(6),
            ClipToBounds = true, Cursor = Cursors.SizeNS, Background = HueStripBrush()
        };
        _hueThumb = new Border
        {
            Width = HueW + 4, Height = 6, CornerRadius = new CornerRadius(3), Background = Brushes.White,
            IsHitTestVisible = false, Margin = new Thickness(-2, 0, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black }
        };
        _hueThumbLayer = new Canvas { IsHitTestVisible = false };
        _hueThumbLayer.Children.Add(_hueThumb);
        var hueGrid = new Grid();
        hueGrid.Children.Add(_hueBox);
        hueGrid.Children.Add(_hueThumbLayer);
        _hueThumbLayer.Width = HueW; _hueThumbLayer.Height = SvH;
        _hueThumbLayer.Margin = new Thickness(12, 0, 0, 0);
        _hueBox.MouseLeftButtonDown += (_, e) => { _hueBox.CaptureMouse(); PickHue(e.GetPosition(_hueBox)); };
        _hueBox.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) PickHue(e.GetPosition(_hueBox)); };
        _hueBox.MouseLeftButtonUp += (_, _) => _hueBox.ReleaseMouseCapture();
        row.Children.Add(hueGrid);
        root.Children.Add(row);

        // --- preview + hex + rgb ---
        var info = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _preview = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), BorderBrush = Res("BorderSoftBrush", "#3A4055") };
        Grid.SetColumn(_preview, 0); info.Children.Add(_preview);

        var fields = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _hexBox = Field("HEX");
        _hexBox.MaxLength = 7;
        var hexRow = LabeledRow("HEX", _hexBox);
        fields.Children.Add(hexRow);
        var rgb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        _rBox = Field("R"); _gBox = Field("G"); _bBox = Field("B");
        rgb.Children.Add(MiniLabeled("R", _rBox));
        rgb.Children.Add(MiniLabeled("G", _gBox));
        rgb.Children.Add(MiniLabeled("B", _bBox));
        fields.Children.Add(rgb);
        Grid.SetColumn(fields, 1); info.Children.Add(fields);
        root.Children.Add(info);

        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        _hexBox.LostFocus += (_, _) => ApplyHex();
        foreach (var b in new[] { _rBox, _gBox, _bBox })
        {
            b.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyRgb(); };
            b.LostFocus += (_, _) => ApplyRgb();
        }

        // --- buttons ---
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = ChromeBtn("Cancel", false);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var ok = ChromeBtn("Select", true);
        ok.Margin = new Thickness(10, 0, 0, 0);
        ok.Click += (_, _) => { ResultHex = CurrentHex(); DialogResult = true; Close(); };
        btns.Children.Add(cancel); btns.Children.Add(ok);
        root.Children.Add(btns);

        Content = root;

        // seed from the initial color
        var c = ParseColor(initialHex) ?? System.Windows.Media.Color.FromRgb(0x1C, 0x3F, 0xAA);
        RgbToHsv(c.R, c.G, c.B, out _h, out _s, out _v);
        Sync();
    }

    // ---------- interaction ----------
    private void PickSV(Point p)
    {
        _s = Math.Clamp(p.X / SvW, 0, 1);
        _v = 1 - Math.Clamp(p.Y / SvH, 0, 1);
        Sync();
    }

    private void PickHue(Point p)
    {
        _h = Math.Clamp(p.Y / SvH, 0, 1) * 360.0;
        Sync();
    }

    private void ApplyHex()
    {
        if (_sync) return;
        var c = ParseColor(_hexBox.Text);
        if (c == null) { Sync(); return; }
        RgbToHsv(c.Value.R, c.Value.G, c.Value.B, out _h, out _s, out _v);
        Sync();
    }

    private void ApplyRgb()
    {
        if (_sync) return;
        if (byte.TryParse(_rBox.Text, out var r) & byte.TryParse(_gBox.Text, out var g) & byte.TryParse(_bBox.Text, out var b))
        {
            RgbToHsv(r, g, b, out _h, out _s, out _v);
        }
        Sync();
    }

    /// <summary>Pushes the current HSV out to every visual + text field.</summary>
    private void Sync()
    {
        _sync = true;
        var hue = HsvToColor(_h, 1, 1);
        _svHue.Fill = new SolidColorBrush(hue);
        Canvas.SetLeft(_svThumb, _s * SvW - _svThumb.Width / 2);
        Canvas.SetTop(_svThumb, (1 - _v) * SvH - _svThumb.Height / 2);
        Canvas.SetTop(_hueThumb, _h / 360.0 * SvH - _hueThumb.Height / 2);

        var col = HsvToColor(_h, _s, _v);
        _preview.Background = new SolidColorBrush(col);
        _hexBox.Text = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
        _rBox.Text = col.R.ToString(); _gBox.Text = col.G.ToString(); _bBox.Text = col.B.ToString();
        _sync = false;
    }

    private string CurrentHex()
    {
        var c = HsvToColor(_h, _s, _v);
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    // ---------- color math ----------
    private static System.Windows.Media.Color HsvToColor(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return System.Windows.Media.Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static void RgbToHsv(byte R, byte G, byte B, out double h, out double s, out double v)
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        s = max <= 0 ? 0 : d / max;
        v = max;
    }

    private static System.Windows.Media.Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Trim();
        if (!hex.StartsWith("#")) hex = "#" + hex;
        if (hex.Length != 7) return null;
        try
        {
            return System.Windows.Media.Color.FromRgb(
                byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber));
        }
        catch { return null; }
    }

    private static LinearGradientBrush HueStripBrush()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i <= 6; i++) g.GradientStops.Add(new GradientStop(HsvToColor(i * 60, 1, 1), i / 6.0));
        g.Freeze();
        return g;
    }

    // ---------- small UI helpers ----------
    private TextBox Field(string name) => new()
    {
        Width = name == "HEX" ? 96 : 46, Height = 30, FontSize = 12.5, TextAlignment = TextAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1),
        Background = Res("SurfaceBrush", "#252A3A"), Foreground = Res("TextPrimaryBrush", "#E6E8EF"),
        BorderBrush = Res("BorderSoftBrush", "#3A4055"), CaretBrush = Res("TextPrimaryBrush", "#E6E8EF"),
        Margin = new Thickness(0, 0, 6, 0)
    };

    private StackPanel LabeledRow(string label, UIElement field)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Width = 30, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("TextMutedBrush", "#8A90A6") });
        sp.Children.Add(field);
        return sp;
    }

    private StackPanel MiniLabeled(string label, UIElement field)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 4, 0) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Res("TextMutedBrush", "#8A90A6") });
        sp.Children.Add(field);
        return sp;
    }

    private Button ChromeBtn(string text, bool primary)
    {
        var b = new Button { Content = text, Height = 36, MinWidth = 92, Cursor = Cursors.Hand, FontSize = 13.5, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(1) };
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        bd.SetValue(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        b.Template = tpl;
        if (primary) { b.Background = Res("AccentBrush", "#3B5BDB"); b.Foreground = Res("AccentTextBrush", "#FFFFFF"); }
        else { b.Background = Res("SurfaceBrush", "#252A3A"); b.Foreground = Res("TextSecondaryBrush", "#C2C7D6"); }
        return b;
    }

    private SolidColorBrush Res(string key, string fallback)
    {
        if (TryFindResource(key) is SolidColorBrush b) return b;
        return (SolidColorBrush)new BrushConverter().ConvertFromString(fallback)!;
    }
}

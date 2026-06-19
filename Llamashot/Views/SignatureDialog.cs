using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using FlowDirection = System.Windows.FlowDirection;

namespace Llamashot.Views;

public class SignatureDialog : Window
{
    private readonly InkCanvas _ink;
    private readonly TextBox _typeBox;
    private readonly TextBlock _typePreview;
    private readonly Border _drawPanel;
    private readonly Border _typePanel;
    private readonly StackPanel _drawButtons;
    private readonly System.Collections.Generic.List<Border> _fontChips = new();
    private readonly System.Collections.Generic.List<(Button btn, bool accent)> _chromeButtons = new();
    private string _mode = "draw";   // draw | type
    private string _typeFont = "Segoe Script";
    private TextBlock? _typeLabel;
    private Border? _previewBox;

    // 5 cursive faces for typed signatures
    private static readonly string[] CursiveFonts =
        { "Segoe Script", "Lucida Handwriting", "Ink Free", "Gabriola", "Brush Script MT" };

    public string? ResultPngPath { get; private set; }

    // Themed brushes, read from the active theme dictionary (refreshed on ThemeChanged).
    private static Brush WindowBg => Res("WindowGradientBrush", Color.FromRgb(0x1A, 0x1A, 0x1E));
    private static Brush Panel => Res("SurfaceBrush", Color.FromRgb(0x24, 0x24, 0x2A));
    private static Brush Accent => Res("AccentBrush", Color.FromRgb(0x42, 0xA5, 0xF5));
    private static Brush Line => Res("BorderSoftBrush", Color.FromRgb(0x3A, 0x3A, 0x42));
    private static Brush TextPrimary => Res("TextPrimaryBrush", Colors.White);
    private static Brush TextSecondary => Res("TextSecondaryBrush", Color.FromRgb(0x99, 0x99, 0x99));

    private static Brush Res(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        return new SolidColorBrush(fallback);
    }

    public SignatureDialog()
    {
        Title = "Signature";
        Width = 600; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = WindowBg;

        Core.ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => Core.ThemeManager.ThemeChanged -= OnThemeChanged;

        var root = new DockPanel { Margin = new Thickness(14) };

        // ---- mode switch ----
        var modeBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(modeBar, Dock.Top);
        modeBar.Children.Add(ModeButton("✍  Draw", "draw"));
        modeBar.Children.Add(ModeButton("⌨  Type", "type"));
        modeBar.Children.Add(ModeButton("\U0001F5BC  Upload", "upload"));
        root.Children.Add(modeBar);

        // ---- bottom buttons ----
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);

        _drawButtons = new StackPanel { Orientation = Orientation.Horizontal };
        var clearBtn = ChromeButton("Clear", false);
        clearBtn.Click += (_, _) => _ink!.Strokes.Clear();
        _drawButtons.Children.Add(clearBtn);
        buttons.Children.Add(_drawButtons);

        var okBtn = ChromeButton("Use signature", true);
        okBtn.Click += Ok_Click;
        var cancelBtn = ChromeButton("Cancel", false);
        cancelBtn.Click += (_, _) => { DialogResult = false; };
        buttons.Children.Add(okBtn);
        buttons.Children.Add(cancelBtn);
        root.Children.Add(buttons);

        // ---- draw panel ----
        _ink = new InkCanvas { Background = Brushes.White };
        _ink.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Black, Width = 3, Height = 3, FitToCurve = true };
        _drawPanel = new Border { Child = _ink, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };

        // ---- type panel ----
        var typeStack = new StackPanel();
        _typeBox = new TextBox
        {
            FontSize = 15, Padding = new Thickness(11, 9, 11, 9), Margin = new Thickness(0, 0, 0, 12),
            Background = Panel, Foreground = TextPrimary,
            BorderBrush = Line, BorderThickness = new Thickness(1),
            CaretBrush = TextPrimary
        };
        _typeBox.TextChanged += (_, _) => UpdateTypePreview();
        _typeLabel = new TextBlock { Text = "Type your name", Foreground = TextSecondary, FontSize = 12, Margin = new Thickness(2, 0, 0, 6) };
        typeStack.Children.Add(_typeLabel);
        typeStack.Children.Add(_typeBox);

        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var f in CursiveFonts) chips.Children.Add(FontChip(f));
        typeStack.Children.Add(chips);

        var previewBox = _previewBox = new Border
        {
            Height = 110, Background = Brushes.White, CornerRadius = new CornerRadius(8),
            BorderBrush = Line, BorderThickness = new Thickness(1)
        };
        _typePreview = new TextBlock
        {
            Text = "", FontSize = 46, Foreground = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily(_typeFont)
        };
        previewBox.Child = _typePreview;
        typeStack.Children.Add(previewBox);

        _typePanel = new Border { Child = typeStack, Visibility = Visibility.Collapsed };

        var body = new Grid();
        body.Children.Add(_drawPanel);
        body.Children.Add(_typePanel);
        root.Children.Add(body);

        Content = root;
        HighlightFontChips();
        ApplyMode();
    }

    // ----------------------------------------------------------------
    private void UpdateTypePreview()
    {
        _typePreview.Text = string.IsNullOrWhiteSpace(_typeBox.Text) ? "" : _typeBox.Text;
        _typePreview.FontFamily = new FontFamily(_typeFont);
    }

    private Border ModeButton(string label, string mode)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(15, 8, 15, 8), Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand, Background = Panel, BorderThickness = new Thickness(1),
            BorderBrush = Line, Tag = mode
        };
        var tb = new TextBlock { Text = label, Foreground = TextPrimary, FontSize = 13 };
        b.Child = tb;
        b.MouseLeftButtonUp += (_, _) =>
        {
            if (mode == "upload") { Upload_Click(this, new RoutedEventArgs()); return; }
            _mode = mode; ApplyMode();
        };
        return b;
    }

    private void ApplyMode()
    {
        _drawPanel.Visibility = _mode == "draw" ? Visibility.Visible : Visibility.Collapsed;
        _typePanel.Visibility = _mode == "type" ? Visibility.Visible : Visibility.Collapsed;
        _drawButtons.Visibility = _mode == "draw" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var child in ((StackPanel)((DockPanel)Content).Children[0]).Children)
            if (child is Border mb && mb.Tag is string m)
            {
                bool on = m == _mode;
                mb.Background = on ? Accent : Panel;
                mb.BorderBrush = on ? Accent : Line;
                if (mb.Child is TextBlock mtb) mtb.Foreground = on ? Res("AccentTextBrush", Colors.White) : TextPrimary;
            }
    }

    private Border FontChip(string font)
    {
        var b = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 8),
            Cursor = Cursors.Hand, Background = Panel, BorderThickness = new Thickness(1),
            BorderBrush = Line, Tag = font
        };
        b.Child = new TextBlock { Text = font, FontSize = 17, Foreground = TextPrimary, FontFamily = new FontFamily(font) };
        b.MouseLeftButtonUp += (_, _) => { _typeFont = font; HighlightFontChips(); UpdateTypePreview(); };
        _fontChips.Add(b);
        return b;
    }

    private void HighlightFontChips()
    {
        foreach (var c in _fontChips)
        {
            bool on = (string)c.Tag! == _typeFont;
            c.Background = on ? Accent : Panel;
            c.BorderBrush = on ? Accent : Line;
            if (c.Child is TextBlock ctb) ctb.Foreground = on ? Res("AccentTextBrush", Colors.White) : TextPrimary;
        }
    }

    private Button ChromeButton(string text, bool accent)
    {
        var btn = new Button
        {
            Content = text, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(16, 7, 16, 7),
            Cursor = Cursors.Hand, FontSize = 13,
            BorderThickness = new Thickness(accent ? 0 : 1)
        };
        SkinChromeButton(btn, accent);
        _chromeButtons.Add((btn, accent));
        return btn;
    }

    private void SkinChromeButton(Button btn, bool accent)
    {
        btn.Foreground = accent ? Res("AccentTextBrush", Colors.White) : TextPrimary;
        var bg = new FrameworkElementFactory(typeof(Border));
        bg.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        bg.SetValue(Border.BackgroundProperty, accent ? Accent : Panel);
        bg.SetValue(Border.BorderBrushProperty, Line);
        bg.SetValue(Border.BorderThicknessProperty, new Thickness(accent ? 0 : 1));
        bg.SetValue(Border.PaddingProperty, new Thickness(16, 7, 16, 7));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        bg.AppendChild(cp);
        btn.Template = new ControlTemplate(typeof(Button)) { VisualTree = bg };
    }

    private void OnThemeChanged()
    {
        Background = WindowBg;
        if (_drawPanel != null) _drawPanel.BorderBrush = Line;
        if (_typeBox != null)
        {
            _typeBox.Background = Panel;
            _typeBox.Foreground = TextPrimary;
            _typeBox.BorderBrush = Line;
            _typeBox.CaretBrush = TextPrimary;
        }
        if (_typeLabel != null) _typeLabel.Foreground = TextSecondary;
        if (_previewBox != null) _previewBox.BorderBrush = Line;
        foreach (var (btn, accent) in _chromeButtons) SkinChromeButton(btn, accent);
        HighlightFontChips();
        ApplyMode();
    }

    private void Upload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg" };
        if (dlg.ShowDialog() == true)
        {
            ResultPngPath = dlg.FileName;
            DialogResult = true;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == "type") { SaveTypedSignature(); return; }

        if (_ink.Strokes.Count == 0) { MessageBox.Show("Draw a signature or switch to Type / Upload."); return; }
        var b = _ink.Strokes.GetBounds();
        if (b.IsEmpty || b.Width < 1 || b.Height < 1) { MessageBox.Show("Draw a signature first."); return; }
        int margin = 8;
        int w = (int)Math.Ceiling(b.Right) + margin;
        int h = (int)Math.Ceiling(b.Bottom) + margin;
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            foreach (var s in _ink.Strokes) s.Draw(dc);
        rtb.Render(visual);
        SavePng(rtb);
    }

    private void SaveTypedSignature()
    {
        string text = _typeBox.Text?.Trim() ?? "";
        if (text.Length == 0) { MessageBox.Show("Type your name first."); return; }

        const double emSize = 64;
        var tf = new Typeface(new FontFamily(_typeFont), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, tf, emSize, Brushes.Black, 1.0);
        int margin = 16;
        int w = (int)Math.Ceiling(ft.WidthIncludingTrailingWhitespace) + margin * 2;
        int h = (int)Math.Ceiling(ft.Height) + margin * 2;
        var rtb = new RenderTargetBitmap(Math.Max(1, w), Math.Max(1, h), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawText(ft, new Point(margin, margin));
        rtb.Render(visual);
        SavePng(rtb);
    }

    private void SavePng(RenderTargetBitmap rtb)
    {
        string path = Path.Combine(Path.GetTempPath(), "fillsign_sig_" + Guid.NewGuid().ToString("N") + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(path)) enc.Save(fs);
        ResultPngPath = path;
        DialogResult = true;
    }
}

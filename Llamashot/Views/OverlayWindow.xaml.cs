using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Models;
using Llamashot.Tools;
using Microsoft.Win32;

namespace Llamashot.Views;

public partial class OverlayWindow : Window
{
    private BitmapSource? _screenshot;
    private Rect _virtualBounds;

    // Interaction state
    private enum Interaction { None, ToolbarIdle, Selecting, Resizing, Moving, Drawing, OcrSelecting }
    internal enum CaptureMode { Screenshot, Video, Ocr, Scroll }
    private Interaction _interaction = Interaction.None;
    private bool _hasSelection;
    private Point _selStart, _selEnd;
    private Rect _selection;

    // Resize
    private enum Handle { None, TL, T, TR, R, BR, B, BL, L }
    private Handle _activeHandle = Handle.None;
    private Point _dragStart;
    private Rect _dragOrigSelection;

    // Pan (Space + drag, like Photoshop)
    private bool _spaceHeld;

    // Full-region toggle (double-click)
    private Rect _originalSelection;
    private bool _isFullRegion;
    private DateTime _lastEscTime = DateTime.MinValue;

    // OCR sub-selection
    private bool _ocrMode;
    private Point _ocrStart;
    private System.Windows.Shapes.Rectangle? _ocrRect;

    // Drawing
    private IDrawingTool? _currentTool;
    private string? _currentToolTag;
    private readonly Stack<DrawingAction> _undoStack = new();
    private readonly Stack<DrawingAction> _redoStack = new();
    private Color _currentColor = Colors.Yellow;
    private double _currentThickness = 2;

    // Snipping toolbar state
    private CaptureMode _captureMode = CaptureMode.Screenshot;
    private int _delaySeconds = 0;

    private static readonly Dictionary<string, Color> ToolColors = new()
    {
        { "Pen", Color.FromRgb(0xFF, 0xA7, 0x26) },       // orange
        { "Line", Color.FromRgb(0x64, 0xB5, 0xF6) },      // light blue
        { "Arrow", Color.FromRgb(0x26, 0xC6, 0xDA) },     // cyan
        { "Rectangle", Color.FromRgb(0x42, 0xA5, 0xF5) }, // blue
        { "Ellipse", Color.FromRgb(0xAB, 0x47, 0xBC) },   // purple
        { "Text", Color.FromRgb(0xFF, 0xCA, 0x28) },      // yellow
        { "Marker", Color.FromRgb(0xFF, 0xEE, 0x58) },    // yellow
        { "Blur", Color.FromRgb(0x78, 0x90, 0x9C) },      // gray
        { "Check", Color.FromRgb(0x4C, 0xAF, 0x50) },     // green
        { "CrossMark", Color.FromRgb(0xF4, 0x43, 0x36) }, // red
        { "Eraser", Color.FromRgb(0xEF, 0x53, 0x50) },    // pink
    };

    private static readonly Color[] PaletteColors = {
        Colors.Red, Colors.OrangeRed, Colors.Orange, Colors.Gold,
        Colors.Yellow, Colors.GreenYellow, Colors.LimeGreen, Colors.Green,
        Colors.Teal, Colors.DodgerBlue, Colors.Blue, Colors.DarkBlue,
        Colors.Purple, Colors.Magenta, Colors.DeepPink, Colors.HotPink,
        Colors.White, Colors.LightGray, Colors.Gray, Colors.DarkGray,
        Colors.Black, Colors.Brown, Colors.SaddleBrown, Colors.Maroon
    };

    public OverlayWindow()
    {
        InitializeComponent();
        InitializeColorPalette();
        InitializeThicknessPopup();
        _currentThickness = 3;
        ThicknessLabel.Text = "3";
        InitializeDelayPopup();

        // Load persisted color
        try
        {
            var cc = (Color)System.Windows.Media.ColorConverter.ConvertFromString(AppSettings.Instance.DefaultColor);
            _currentColor = cc;
            ColorIndicator.Fill = new SolidColorBrush(_currentColor);
        }
        catch { /* keep default yellow */ }
    }

    internal void StartCapture(CaptureMode initialMode = CaptureMode.Screenshot)
    {
        _virtualBounds = ScreenCapture.GetVirtualScreenBounds();
        _screenshot = ScreenCapture.CaptureFullScreen();
        ScreenshotImage.Source = _screenshot;

        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;

        UpdateDimming(Rect.Empty);

        _interaction = Interaction.ToolbarIdle;
        _captureMode = initialMode;
        _hasSelection = false;
        _currentTool = null;
        _currentToolTag = null;
        SelectionBorder.Visibility = Visibility.Collapsed;
        OcrDashBorder.Visibility = Visibility.Collapsed;
        DimensionBorder.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Collapsed;
        VideoToolbarCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;

        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();

        // Show snipping toolbar
        SnippingToolbarCanvas.Visibility = Visibility.Visible;
        Cursor = Cursors.Cross;

        Show();
        Activate();
        Focus();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, PositionSnippingToolbar);
    }

    private void InitializeColorPalette()
    {
        foreach (var color in PaletteColors)
        {
            var swatch = new Border
            {
                Width = 24, Height = 24,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (s, e) =>
            {
                _currentColor = color;
                ColorIndicator.Fill = new SolidColorBrush(color);
                if (_currentTool != null) _currentTool.StrokeColor = color;
                ColorPaletteCanvas.Visibility = Visibility.Collapsed;
                // Persist color choice
                AppSettings.Instance.DefaultColor = color.ToString();
                AppSettings.Save();
                e.Handled = true;
            };
            ColorSwatches.Children.Add(swatch);
        }
    }

    private void InitializeThicknessPopup()
    {
        for (int i = 1; i <= 10; i++)
        {
            int val = i;
            var btn = new Button
            {
                Width = 36, Height = 28,
                Content = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Shapes.Ellipse
                        {
                            Width = Math.Max(4, val * 1.6),
                            Height = Math.Max(4, val * 1.6),
                            Fill = Brushes.White,
                            Margin = new Thickness(2, 0, 4, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = val.ToString(),
                            Foreground = Brushes.White,
                            FontSize = 12,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Focusable = false,
                Margin = new Thickness(1)
            };
            btn.Click += (s, e) =>
            {
                _currentThickness = val;
                ThicknessLabel.Text = val.ToString();
                if (_currentTool != null) _currentTool.Thickness = val;
                ThicknessPopupCanvas.Visibility = Visibility.Collapsed;
            };
            ThicknessOptions.Children.Add(btn);
        }
    }

    private void InitializeDelayPopup()
    {
        foreach (var (label, seconds) in new[] { ("No delay", 0), ("1 second", 1), ("3 seconds", 3), ("5 seconds", 5), ("10 seconds", 10) })
        {
            int val = seconds;
            string text = label;
            var btn = new Button
            {
                Width = 100, Height = 26,
                Content = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 12 },
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Focusable = false,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new Thickness(8, 0, 0, 0)
            };
            btn.Click += (s, e) =>
            {
                _delaySeconds = val;
                DelayLabel.Text = val > 0 ? $"{val}s" : "No delay";
                DelayPopupCanvas.Visibility = Visibility.Collapsed;
            };
            DelayOptions.Children.Add(btn);
        }
    }

    private void PositionSnippingToolbar()
    {
        SnippingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = SnippingToolbar.DesiredSize.Width;
        double left = (ActualWidth - w) / 2;
        double top = 40;
        Canvas.SetLeft(SnippingToolbar, left);
        Canvas.SetTop(SnippingToolbar, top);
        UpdateModeIndicator();
    }

    private void UpdateModeIndicator()
    {
        Button activeBtn = _captureMode switch
        {
            CaptureMode.Video => BtnModeVideo,
            CaptureMode.Ocr => BtnModeOcr,
            CaptureMode.Scroll => BtnModeScroll,
            _ => BtnModeScreenshot
        };
        var color = _captureMode switch
        {
            CaptureMode.Video => Color.FromRgb(0xF4, 0x43, 0x36),
            CaptureMode.Ocr => Color.FromRgb(0x26, 0xC6, 0xDA),
            CaptureMode.Scroll => Color.FromRgb(0xFF, 0xA7, 0x26),
            _ => Color.FromRgb(0x21, 0x96, 0xF3)
        };
        ModeIndicator.Background = new SolidColorBrush(color);

        var btnPos = activeBtn.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(ModeIndicator, btnPos.X + (activeBtn.ActualWidth - 22) / 2);
        Canvas.SetTop(ModeIndicator, btnPos.Y + activeBtn.ActualHeight + 2);

        UpdateModeButtonColors();
    }

    private void UpdateModeButtonColors()
    {
        var screenshotBrush = new SolidColorBrush(_captureMode == CaptureMode.Screenshot ? Color.FromRgb(0x64, 0xB5, 0xF6) : Color.FromRgb(0x88, 0x88, 0x88));
        var videoBrush = new SolidColorBrush(_captureMode == CaptureMode.Video ? Color.FromRgb(0xF4, 0x43, 0x36) : Color.FromRgb(0x88, 0x88, 0x88));
        var ocrBrush = new SolidColorBrush(_captureMode == CaptureMode.Ocr ? Color.FromRgb(0x26, 0xC6, 0xDA) : Color.FromRgb(0x88, 0x88, 0x88));

        // Update icon colors in the StackPanel > Canvas
        var ssPanel = (StackPanel)BtnModeScreenshot.Content;
        foreach (var child in ((Canvas)ssPanel.Children[0]).Children)
        {
            if (child is System.Windows.Shapes.Rectangle r) r.Stroke = screenshotBrush;
            if (child is Ellipse el) el.Stroke = screenshotBrush;
        }
        LblScreenshot.Foreground = screenshotBrush;

        var vidPanel = (StackPanel)BtnModeVideo.Content;
        foreach (var child in ((Canvas)vidPanel.Children[0]).Children)
        {
            if (child is System.Windows.Shapes.Rectangle r) r.Stroke = videoBrush;
            if (child is System.Windows.Shapes.Path p) p.Fill = videoBrush;
        }
        LblVideo.Foreground = videoBrush;

        // OCR icon colors stay distinct but label color changes
        LblOcr.Foreground = ocrBrush;

        var scrollBrush = new SolidColorBrush(_captureMode == CaptureMode.Scroll ? Color.FromRgb(0xFF, 0xA7, 0x26) : Color.FromRgb(0x88, 0x88, 0x88));
        LblScroll.Foreground = scrollBrush;
        // Update scroll icon colors
        var scrollPanel = (StackPanel)BtnModeScroll.Content;
        foreach (var child in ((Canvas)scrollPanel.Children[0]).Children)
        {
            if (child is System.Windows.Shapes.Rectangle r) r.Stroke = scrollBrush;
            if (child is Line ln) ln.Stroke = scrollBrush;
            if (child is System.Windows.Shapes.Path p) p.Stroke = scrollBrush;
        }
    }

    // ============ SNIPPING TOOLBAR HANDLERS ============

    private void ModeScreenshot_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Screenshot;
        OnModeChanged();
    }

    private void ModeVideo_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Video;
        OnModeChanged();
    }

    private void ModeOcr_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Ocr;
        OnModeChanged();
    }

    private void ModeScroll_Click(object sender, RoutedEventArgs e)
    {
        _captureMode = CaptureMode.Scroll;
        OnModeChanged();
    }

    private void ScrollToggle_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Instance;
        s.ScrollAutoMode = !s.ScrollAutoMode;
        AppSettings.Save();
        UpdateScrollToggleUI();
    }

    private void UpdateScrollToggleUI()
    {
        bool auto = AppSettings.Instance.ScrollAutoMode;
        LblScrollToggle.Text = auto ? "Auto" : "Manual";
        LblScrollToggle.Foreground = new SolidColorBrush(auto
            ? Color.FromRgb(0xFF, 0xA7, 0x26)
            : Color.FromRgb(0x26, 0xC6, 0xDA));
    }

    private void OnModeChanged()
    {
        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        UpdateModeIndicator();

        // Show scroll toggle only in scroll mode
        BtnScrollToggle.Visibility = _captureMode == CaptureMode.Scroll
            ? Visibility.Visible : Visibility.Collapsed;
        if (_captureMode == CaptureMode.Scroll) UpdateScrollToggleUI();

        Cursor = Cursors.Cross;
    }

    private void Delay_Click(object sender, RoutedEventArgs e)
    {

        if (DelayPopupCanvas.Visibility == Visibility.Visible)
        {
            DelayPopupCanvas.Visibility = Visibility.Collapsed;
            return;
        }
        var btnPos = BtnDelay.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(DelayPopup, btnPos.X);
        Canvas.SetTop(DelayPopup, btnPos.Y + BtnDelay.ActualHeight + 4);
        DelayPopupCanvas.Visibility = Visibility.Visible;
    }

    // ============ FULLSCREEN / VIDEO / OCR EXECUTION ============

    private void ExecuteFullscreenCapture()
    {
        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;

        DelayPopupCanvas.Visibility = Visibility.Collapsed;

        if (_delaySeconds > 0)
        {
            ExecuteWithDelay(() => ExecuteFullscreenCapture_Inner());
            return;
        }

        ExecuteFullscreenCapture_Inner();
    }

    private void SetSelectionAndShowToolbar(Rect rect, bool isFullRegion = false)
    {
        _selStart = new Point(rect.X, rect.Y);
        _selEnd = new Point(rect.Right, rect.Bottom);
        _selection = rect;
        _hasSelection = true;
        _isFullRegion = isFullRegion;
        _interaction = Interaction.None;

        SelectionBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionBorder, rect.X);
        Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;

        DimensionText.Text = $"{(int)rect.Width} x {(int)rect.Height}";
        Canvas.SetLeft(DimensionBorder, rect.X);
        Canvas.SetTop(DimensionBorder, Math.Max(0, rect.Y - 28));
        DimensionBorder.Visibility = Visibility.Visible;

        UpdateDimming(_selection);

        if (_captureMode == CaptureMode.Video)
        {
            ShowVideoToolbar();
            ShowResizeHandles();
        }
        else
        {
            ShowToolbars();
            ShowResizeHandles();
        }
        Focus();
    }

    private void ExecuteFullscreenCapture_Inner()
    {
        if (_captureMode == CaptureMode.Video)
        {
            SetSelectionAndShowToolbar(new Rect(0, 0, ActualWidth, ActualHeight), isFullRegion: true);
        }
        else if (_captureMode == CaptureMode.Ocr)
        {
            PerformDirectOcr(new Rect(0, 0, ActualWidth, ActualHeight));
        }
        else
        {
            SetSelectionAndShowToolbar(new Rect(0, 0, ActualWidth, ActualHeight), isFullRegion: true);
        }
    }

    private void LaunchVideoRecording(Rect dipRegion)
    {
        var dpi = GetDpiScale();
        int px = (int)(dipRegion.X * dpi);
        int py = (int)(dipRegion.Y * dpi);
        int pw = (int)(dipRegion.Width * dpi);
        int ph = (int)(dipRegion.Height * dpi);

        double dx = dipRegion.X + Left;
        double dy = dipRegion.Y + Top;

        Hide();

        var overlay = new RecordingOverlay(px, py, pw, ph, dx, dy, dipRegion.Width, dipRegion.Height);
        overlay.Show();
        Close();
    }

    private async void ExecuteWithDelay(Action afterDelay)
    {
        Hide();

        for (int i = _delaySeconds; i > 0; i--)
            await Task.Delay(1000);

        _screenshot = ScreenCapture.CaptureFullScreen();
        ScreenshotImage.Source = _screenshot;

        Show();
        Activate();
        Focus();

        afterDelay();
    }

    private async void LaunchScrollCapture(Point clickPos)
    {
        // Get the window under the cursor (screen coordinates)
        var dpi = GetDpiScale();
        int screenX = (int)((clickPos.X + Left) * dpi);
        int screenY = (int)((clickPos.Y + Top) * dpi);
        var pt = new NativeMethods.POINT { X = screenX, Y = screenY };
        IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return;
        hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        if (hwnd == IntPtr.Zero) return;

        Hide();

        bool autoMode = AppSettings.Instance.ScrollAutoMode;
        var scrollCapture = new ScrollCapture(hwnd);

        if (autoMode)
        {
            var cts = new CancellationTokenSource();

            // Show progress window with Cancel/Esc support
            var progressWindow = new Window
            {
                Title = "Llamashot - Scroll Capture",
                Width = 320, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true, ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
                WindowStyle = WindowStyle.ToolWindow
            };
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
            var txt = new TextBlock
            {
                Text = "Auto scrolling... Frames: 0",
                Foreground = Brushes.White, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            var cancelBtn = new Button
            {
                Content = "Stop (Esc)", Width = 100, Height = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
                Cursor = Cursors.Hand
            };
            cancelBtn.Click += (s, ev) => { cts.Cancel(); progressWindow.Close(); };
            panel.Children.Add(txt);
            panel.Children.Add(cancelBtn);
            progressWindow.Content = panel;
            progressWindow.PreviewKeyDown += (s, ev) =>
            {
                if (ev.Key == Key.Escape) { cts.Cancel(); progressWindow.Close(); }
            };

            scrollCapture.FrameCaptured += count =>
                Dispatcher.Invoke(() => { if (!cts.IsCancellationRequested) txt.Text = $"Auto scrolling... Frames: {count}"; });

            progressWindow.Show();
            // Exclude from capture immediately (handle exists after Show)
            var pwh = new System.Windows.Interop.WindowInteropHelper(progressWindow).Handle;
            NativeMethods.SetWindowDisplayAffinity(pwh, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            BitmapSource? result = null;
            try { result = await scrollCapture.AutoCaptureAsync(null, cts.Token); }
            catch (OperationCanceledException) { /* user cancelled */ }

            if (progressWindow.IsVisible) progressWindow.Close();

            if (result != null)
            {
                var preview = new ScrollPreviewWindow(result);
                preview.Show();
            }
        }
        else
        {
            // Manual scroll capture
            NativeMethods.SetForegroundWindow(hwnd);

            scrollCapture.StartManualCapture();

            // Show a small instruction window
            var doneWindow = new Window
            {
                Title = "Llamashot - Scroll Capture",
                Width = 320, Height = 120,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
                WindowStyle = WindowStyle.ToolWindow,
                SizeToContent = SizeToContent.Height
            };
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
            var txt = new TextBlock
            {
                Text = $"Scroll the window now... Frames: {scrollCapture.FrameCount}",
                Foreground = Brushes.White, FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            scrollCapture.FrameCaptured += count =>
                Dispatcher.Invoke(() => txt.Text = $"Scroll the window... Frames: {count}");

            var doneBtn = new Button
            {
                Content = "Done", Width = 80, Height = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 8, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)),
                Foreground = Brushes.White, Cursor = Cursors.Hand
            };
            doneBtn.Click += (s, ev) => doneWindow.DialogResult = true;
            panel.Children.Add(txt);
            panel.Children.Add(doneBtn);
            doneWindow.Content = panel;
            doneWindow.PreviewKeyDown += (s, ev) =>
            {
                if (ev.Key == Key.Enter || ev.Key == Key.Escape)
                    doneWindow.DialogResult = true;
            };

            doneWindow.Show();
            // Exclude from capture immediately
            var dwh = new System.Windows.Interop.WindowInteropHelper(doneWindow).Handle;
            NativeMethods.SetWindowDisplayAffinity(dwh, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            doneWindow.Hide();
            doneWindow.ShowDialog();

            var result = scrollCapture.FinishManualCapture();
            if (result != null)
            {
                var preview = new ScrollPreviewWindow(result);
                preview.Show();
            }
        }

        Close();
    }

    private async void PerformDirectOcr(Rect dipRegion)
    {
        if (_screenshot == null) return;

        double dpi = GetDpiScale();
        int x = Math.Max(0, (int)(dipRegion.X * dpi));
        int y = Math.Max(0, (int)(dipRegion.Y * dpi));
        int w = (int)(dipRegion.Width * dpi);
        int h = (int)(dipRegion.Height * dpi);

        w = Math.Min(w, _screenshot.PixelWidth - x);
        h = Math.Min(h, _screenshot.PixelHeight - y);

        if (w < 5 || h < 5) { Close(); return; }

        var region = new Int32Rect(x, y, w, h);
        var cropped = ScreenCapture.CropBitmap(_screenshot, region);

        try
        {
            var text = await Core.OcrHelper.ExtractTextAsync(cropped);
            if (!string.IsNullOrWhiteSpace(text))
            {
                Clipboard.SetText(text);
                // Show brief "Copied" overlay before closing
                ShowCopiedOverlay();
                await Task.Delay(800);
            }
        }
        catch { }

        Close();
    }

    private void ShowCopiedOverlay()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xDD, 0x11, 0x11, 0x11)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 10, 20, 10),
            IsHitTestVisible = false
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, Opacity = 0.5, ShadowDepth = 2 };
        var panel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = "\u2714",
            Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            FontSize = 18, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Copied to clipboard",
            Foreground = Brushes.White,
            FontSize = 14, VerticalAlignment = VerticalAlignment.Center
        });
        border.Child = panel;

        Canvas.SetLeft(border, (ActualWidth - 200) / 2);
        Canvas.SetTop(border, (ActualHeight - 40) / 2);
        MainCanvas.Children.Add(border);
    }

    // ============ UNIFIED MOUSE HANDLING ON MainCanvas ============

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainCanvas);
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;

        DelayPopupCanvas.Visibility = Visibility.Collapsed;

        // ToolbarIdle: clicking on dimmed area starts region selection (or window select for scroll mode)
        if (_interaction == Interaction.ToolbarIdle)
        {
                // Scroll mode: click to select window, then launch scroll capture
                if (_captureMode == CaptureMode.Scroll)
                {
                    LaunchScrollCapture(pos);
                    e.Handled = true;
                    return;
                }

                if (_delaySeconds > 0)
                {
                    ExecuteWithDelay(() =>
                    {
                        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;
                        _interaction = Interaction.ToolbarIdle;
                        Cursor = Cursors.Cross;
                    });
                    e.Handled = true;
                    return;
                }

                SnippingToolbarCanvas.Visibility = Visibility.Collapsed;

                _interaction = Interaction.Selecting;
                _selStart = pos;
                _selEnd = pos;
                MainCanvas.CaptureMouse();
                SelectionBorder.Visibility = Visibility.Visible;
                DimensionBorder.Visibility = Visibility.Visible;
                UpdateSelectionVisuals();
                e.Handled = true;
                return;
        }

        if (_hasSelection)
        {
            // Double-click: toggle fullscreen selection
            if (e.ClickCount == 2 && _selection.Contains(pos))
            {
                ToggleFullRegion();
                e.Handled = true;
                return;
            }

            // Priority 0: Space held = pan (Photoshop-style)
            if (_spaceHeld && _selection.Contains(pos))
            {
                _interaction = Interaction.Moving;
                _dragStart = pos;
                _dragOrigSelection = _selection;
                MainCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Priority 1: Resize handle
            var handle = HitTestHandle(pos);
            if (handle != Handle.None)
            {
                _interaction = Interaction.Resizing;
                _activeHandle = handle;
                _dragStart = pos;
                _dragOrigSelection = _selection;
                MainCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Priority 2: Inside selection
            if (_selection.Contains(pos))
            {
                if (_currentTool != null)
                {
                    // Delegate to drawing canvas
                    return; // Let DrawingCanvas handle it
                }
                else
                {
                    // No tool selected = move
                    _interaction = Interaction.Moving;
                    _dragStart = pos;
                    _dragOrigSelection = _selection;
                    MainCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }

            // Priority 3: Outside selection - new selection
            HideToolbars();
            DrawingCanvas.Children.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            _hasSelection = false;
            _isFullRegion = false;
            _currentTool = null;
            _currentToolTag = null;
        }

        // Start new selection
        _interaction = Interaction.Selecting;
        _selStart = pos;
        _selEnd = pos;
        MainCanvas.CaptureMouse();

        SelectionBorder.Visibility = Visibility.Visible;
        DimensionBorder.Visibility = Visibility.Visible;
        UpdateSelectionVisuals();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(MainCanvas);

        switch (_interaction)
        {
            case Interaction.ToolbarIdle:
                break;

            case Interaction.Selecting:
                _selEnd = pos;
                UpdateSelectionVisuals();
                break;

            case Interaction.Resizing:
                ApplyResize(pos);
                break;

            case Interaction.Moving:
                ApplyMove(pos);
                break;

            case Interaction.None when _hasSelection:
                // Update cursor based on position
                var handle = HitTestHandle(pos);
                if (handle != Handle.None)
                {
                    Cursor = handle switch
                    {
                        Handle.TL or Handle.BR => Cursors.SizeNWSE,
                        Handle.TR or Handle.BL => Cursors.SizeNESW,
                        Handle.T or Handle.B => Cursors.SizeNS,
                        Handle.L or Handle.R => Cursors.SizeWE,
                        _ => Cursors.Cross
                    };
                }
                else if (_selection.Contains(pos))
                {
                    Cursor = _currentTool != null ? _currentTool.Cursor : Cursors.SizeAll;
                }
                else
                {
                    Cursor = Cursors.Cross;
                }
                break;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var interaction = _interaction;
        _interaction = Interaction.None;
        MainCanvas.ReleaseMouseCapture();

        switch (interaction)
        {
            case Interaction.Selecting:
                _selEnd = e.GetPosition(MainCanvas);
                UpdateSelectionVisuals();
                if (_selection.Width > 5 && _selection.Height > 5)
                {
                    if (_captureMode == CaptureMode.Ocr)
                    {
                        PerformDirectOcr(_selection);
                        return;
                    }
                    SetSelectionAndShowToolbar(_selection);
                }
                break;

            case Interaction.Resizing:
            case Interaction.Moving:
                ShowResizeHandles();
                UpdateToolbarPositions();
                break;
        }

        Focus(); // Restore keyboard focus for shortcuts
    }

    private void UpdateSelectionVisuals()
    {
        double x = Math.Clamp(Math.Min(_selStart.X, _selEnd.X), 0, ActualWidth);
        double y = Math.Clamp(Math.Min(_selStart.Y, _selEnd.Y), 0, ActualHeight);
        double x2 = Math.Clamp(Math.Max(_selStart.X, _selEnd.X), 0, ActualWidth);
        double y2 = Math.Clamp(Math.Max(_selStart.Y, _selEnd.Y), 0, ActualHeight);
        double w = x2 - x;
        double h = y2 - y;

        _selection = new Rect(x, y, w, h);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = w;
        SelectionBorder.Height = h;

        // OCR mode: show dashed border instead of solid
        if (_captureMode == CaptureMode.Ocr)
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(OcrDashBorder, x);
            Canvas.SetTop(OcrDashBorder, y);
            OcrDashBorder.Width = w;
            OcrDashBorder.Height = h;
            OcrDashBorder.Visibility = Visibility.Visible;
        }
        else
        {
            SelectionBorder.Visibility = Visibility.Visible;
            OcrDashBorder.Visibility = Visibility.Collapsed;
        }

        DimensionText.Text = $"{(int)w} x {(int)h}";
        Canvas.SetLeft(DimensionBorder, x);
        Canvas.SetTop(DimensionBorder, Math.Max(0, y - 28));
        DimensionBorder.Visibility = Visibility.Visible;

        UpdateDimming(_selection);
    }

    private void UpdateDimming(Rect sel)
    {
        var full = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        if (sel.Width > 0 && sel.Height > 0)
        {
            var hole = new RectangleGeometry(sel);
            DimmingPath.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);
        }
        else
        {
            DimmingPath.Data = full;
        }
    }

    // ============ RESIZE ============

    private const double HandleSize = 8;
    private const double HitMargin = 8;

    private void ShowResizeHandles()
    {
        HandleCanvas.Children.Clear();
        HandleCanvas.Visibility = Visibility.Visible;

        var pts = new (Handle h, double x, double y)[]
        {
            (Handle.TL, _selection.Left, _selection.Top),
            (Handle.T, _selection.Left + _selection.Width / 2, _selection.Top),
            (Handle.TR, _selection.Right, _selection.Top),
            (Handle.R, _selection.Right, _selection.Top + _selection.Height / 2),
            (Handle.BR, _selection.Right, _selection.Bottom),
            (Handle.B, _selection.Left + _selection.Width / 2, _selection.Bottom),
            (Handle.BL, _selection.Left, _selection.Bottom),
            (Handle.L, _selection.Left, _selection.Top + _selection.Height / 2),
        };

        foreach (var (h, cx, cy) in pts)
        {
            var r = new System.Windows.Shapes.Rectangle
            {
                Width = HandleSize, Height = HandleSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                StrokeThickness = 1.5,
                Tag = h,
            };
            Canvas.SetLeft(r, cx - HandleSize / 2);
            Canvas.SetTop(r, cy - HandleSize / 2);
            HandleCanvas.Children.Add(r);
        }
    }

    private Handle HitTestHandle(Point pos)
    {
        foreach (UIElement child in HandleCanvas.Children)
        {
            if (child is System.Windows.Shapes.Rectangle r && r.Tag is Handle h)
            {
                double x = Canvas.GetLeft(r);
                double y = Canvas.GetTop(r);
                if (new Rect(x - HitMargin, y - HitMargin, HandleSize + HitMargin * 2, HandleSize + HitMargin * 2).Contains(pos))
                    return h;
            }
        }
        return Handle.None;
    }

    private void ApplyResize(Point pos)
    {
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;
        var r = _dragOrigSelection;

        double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

        switch (_activeHandle)
        {
            case Handle.TL: left += dx; top += dy; break;
            case Handle.T:  top += dy; break;
            case Handle.TR: right += dx; top += dy; break;
            case Handle.R:  right += dx; break;
            case Handle.BR: right += dx; bottom += dy; break;
            case Handle.B:  bottom += dy; break;
            case Handle.BL: left += dx; bottom += dy; break;
            case Handle.L:  left += dx; break;
        }

        // Clamp to screen bounds and enforce minimum 10x10
        left = Math.Clamp(left, 0, ActualWidth - 10);
        top = Math.Clamp(top, 0, ActualHeight - 10);
        right = Math.Clamp(right, left + 10, ActualWidth);
        bottom = Math.Clamp(bottom, top + 10, ActualHeight);

        _selection = new Rect(left, top, right - left, bottom - top);
        RefreshSelectionUI();
    }

    private void ApplyMove(Point pos)
    {
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        // Clamp to screen bounds
        double newLeft = Math.Clamp(_dragOrigSelection.Left + dx, 0, Math.Max(0, ActualWidth - _dragOrigSelection.Width));
        double newTop = Math.Clamp(_dragOrigSelection.Top + dy, 0, Math.Max(0, ActualHeight - _dragOrigSelection.Height));

        var newSel = new Rect(newLeft, newTop, _dragOrigSelection.Width, _dragOrigSelection.Height);

        // Move all annotations by the delta
        var moveDx = newSel.Left - _selection.Left;
        var moveDy = newSel.Top - _selection.Top;

        foreach (UIElement child in DrawingCanvas.Children)
        {
            if (child is Polyline pl)
            {
                var newPts = new PointCollection();
                foreach (var p in pl.Points)
                    newPts.Add(new Point(p.X + moveDx, p.Y + moveDy));
                pl.Points = newPts;
            }
            else if (child is Line ln)
            {
                ln.X1 += moveDx; ln.Y1 += moveDy;
                ln.X2 += moveDx; ln.Y2 += moveDy;
            }
            else
            {
                var cx = Canvas.GetLeft(child);
                var cy = Canvas.GetTop(child);
                if (!double.IsNaN(cx)) Canvas.SetLeft(child, cx + moveDx);
                if (!double.IsNaN(cy)) Canvas.SetTop(child, cy + moveDy);
            }
        }

        _selection = newSel;
        RefreshSelectionUI();
    }

    private void RefreshSelectionUI()
    {
        Canvas.SetLeft(SelectionBorder, _selection.Left);
        Canvas.SetTop(SelectionBorder, _selection.Top);
        SelectionBorder.Width = _selection.Width;
        SelectionBorder.Height = _selection.Height;

        DimensionText.Text = $"{(int)_selection.Width} x {(int)_selection.Height}";
        Canvas.SetLeft(DimensionBorder, _selection.Left);
        Canvas.SetTop(DimensionBorder, Math.Max(0, _selection.Top - 28));

        UpdateDimming(_selection);
        ShowResizeHandles();
        UpdateDrawingCanvasClip();
        UpdateToolbarPositions();
    }

    // ============ FULL-REGION TOGGLE ============

    private void ToggleFullRegion()
    {
        if (_isFullRegion)
        {
            _selection = _originalSelection;
            _isFullRegion = false;
        }
        else
        {
            _originalSelection = _selection;
            _selection = new Rect(0, 0, ActualWidth, ActualHeight);
            _isFullRegion = true;
        }
        RefreshSelectionUI();
    }

    // ============ TOOLBARS ============

    private void ShowToolbars()
    {
        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;

        DelayPopupCanvas.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Visible;
        DrawingCanvas.Visibility = Visibility.Visible;
        UpdateToolbarPositions();
        UpdateDrawingCanvasClip();
    }

    // ============ VIDEO PRE-START TOOLBAR ============

    private bool _videoMicEnabled;
    private bool _videoSysAudioEnabled;

    private void ShowVideoToolbar()
    {
        SnippingToolbarCanvas.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        VideoToolbarCanvas.Visibility = Visibility.Visible;
        HandleCanvas.Visibility = Visibility.Visible;

        // Restore dimming (video mode removed it during selection)
        if (_screenshot != null)
        {
            ScreenshotImage.Source = _screenshot;
            UpdateDimming(_selection);
        }

        // Position video toolbar at top center
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            VideoToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = VideoToolbar.DesiredSize.Width;
            Canvas.SetLeft(VideoToolbar, (ActualWidth - w) / 2);
            Canvas.SetTop(VideoToolbar, 30);
        });
    }

    private void UpdateVideoMicUI()
    {
        var color = _videoMicEnabled ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0x66, 0x66, 0x66);
        var brush = new SolidColorBrush(color);
        VideoMicBody.Fill = brush;
        VideoMicArc.Stroke = brush;
        VideoMicStand.Stroke = brush;
        VideoMicSlash.Visibility = _videoMicEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateVideoSysAudioUI()
    {
        var color = _videoSysAudioEnabled ? Color.FromRgb(0x4C, 0xAF, 0x50) : Color.FromRgb(0x66, 0x66, 0x66);
        var brush = new SolidColorBrush(color);
        VideoSpeakerBody.Fill = brush;
        VideoSpeakerWave1.Stroke = brush;
        VideoSpeakerWave2.Stroke = brush;
        VideoSpeakerSlash.Visibility = _videoSysAudioEnabled ? Visibility.Collapsed : Visibility.Visible;
    }

    private void VideoMic_Click(object sender, RoutedEventArgs e)
    {
        _videoMicEnabled = !_videoMicEnabled;
        UpdateVideoMicUI();
    }

    private void VideoSysAudio_Click(object sender, RoutedEventArgs e)
    {
        _videoSysAudioEnabled = !_videoSysAudioEnabled;
        UpdateVideoSysAudioUI();
    }

    private async void VideoStart_Click(object sender, RoutedEventArgs e)
    {
        BtnVideoStart.IsEnabled = false;

        // 3-2-1 countdown
        for (int i = 3; i > 0; i--)
        {
            VideoTimer.Text = i.ToString();
            VideoTimer.FontSize = 20;
            await Task.Delay(1000);
        }

        // Launch recording with current selection
        var dpi = GetDpiScale();
        int px = (int)(_selection.X * dpi);
        int py = (int)(_selection.Y * dpi);
        int pw = (int)(_selection.Width * dpi);
        int ph = (int)(_selection.Height * dpi);

        double dx = _selection.X + Left;
        double dy = _selection.Y + Top;

        Hide();

        var overlay = new RecordingOverlay(px, py, pw, ph, dx, dy, _selection.Width, _selection.Height,
            startImmediately: true, micEnabled: _videoMicEnabled, sysAudioEnabled: _videoSysAudioEnabled);
        overlay.Show();
        Close();
    }

    private void HideToolbars()
    {
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        VideoToolbarCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
    }

    private void UpdateToolbarPositions()
    {
        // Adaptive columns: try 1-col, measure, switch to 2-col if it won't fit
        double availableHeight = ActualHeight - 8;

        // Measure in 1-col mode
        ToolWrapPanel.Width = 32;
        BottomToolsPanel.Width = 32;
        DrawingToolbar.InvalidateMeasure();
        DrawingToolbar.UpdateLayout();
        DrawingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        if (DrawingToolbar.DesiredSize.Height > availableHeight)
        {
            // Switch to 2-col
            ToolWrapPanel.Width = 64;
            BottomToolsPanel.Width = 64;
            DrawingToolbar.InvalidateMeasure();
            DrawingToolbar.UpdateLayout();
            DrawingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        ActionToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double dtTop = _selection.Top;

        // Prefer RIGHT side of selection, fallback to left
        double dtLeft = _selection.Right + 8;
        if (dtLeft + DrawingToolbar.DesiredSize.Width > ActualWidth)
            dtLeft = _selection.Left - DrawingToolbar.DesiredSize.Width - 8;
        if (dtLeft < 4)
            dtLeft = 4;

        // Clamp to screen bounds
        if (dtTop + DrawingToolbar.DesiredSize.Height > ActualHeight - 4)
            dtTop = ActualHeight - DrawingToolbar.DesiredSize.Height - 4;
        if (dtTop < 4) dtTop = 4;

        Canvas.SetLeft(DrawingToolbar, dtLeft);
        Canvas.SetTop(DrawingToolbar, dtTop);

        double atLeft = _selection.Right - ActionToolbar.DesiredSize.Width;
        double atTop = _selection.Bottom + 8;
        if (atLeft < 4) atLeft = _selection.Left;
        if (atTop + ActionToolbar.DesiredSize.Height > ActualHeight)
            atTop = _selection.Top - ActionToolbar.DesiredSize.Height - 8;
        // Clamp inside screen (fullscreen: both above/below overflow)
        if (atTop < 4)
            atTop = ActualHeight - ActionToolbar.DesiredSize.Height - 4;

        Canvas.SetLeft(ActionToolbar, atLeft);
        Canvas.SetTop(ActionToolbar, atTop);
    }

    private void UpdateDrawingCanvasClip()
    {
        DrawingCanvas.Clip = new RectangleGeometry(_selection);
    }

    // ============ DRAWING ============

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string toolName) return;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;

        // Finalize any active text box before switching tools
        if (_currentTool is TextTool tt)
            tt.FinalizeActiveTextBox();

        // Toggle: if same tool clicked again, deselect it
        if (_currentToolTag == toolName)
        {
            DeselectTool();
            return;
        }

        // Reset all tool button highlights
        ClearToolHighlights();
        var highlightColor = ToolColors.TryGetValue(toolName, out var tc)
            ? Color.FromArgb(80, tc.R, tc.G, tc.B)
            : Color.FromArgb(80, 33, 150, 243);
        btn.Background = new SolidColorBrush(highlightColor);

        _currentToolTag = toolName;
        _currentTool = toolName switch
        {
            "Pen" => new PenTool(),
            "Line" => new LineTool(),
            "Arrow" => new ArrowTool(),
            "Rectangle" => new RectangleTool(),
            "Ellipse" => new EllipseTool(),
            "Text" => new TextTool(),
            "Marker" => new MarkerTool(),
            "Blur" => new BlurTool { ScreenshotSource = _screenshot },
            "Check" => new StampTool(StampType.Check),
            "CrossMark" => new StampTool(StampType.Cross),
            "Eraser" => new EraserTool(),
            _ => null
        };

        if (_currentTool != null)
        {
            _currentTool.StrokeColor = _currentColor;
            _currentTool.Thickness = _currentThickness;
            DrawingCanvas.Cursor = _currentTool.Cursor;
        }

        Focus();
    }

    private void ClearToolHighlights()
    {
        // Buttons are inside WrapPanel, not direct children of DrawingToolsPanel
        foreach (UIElement child in ToolWrapPanel.Children)
            if (child is Button b) b.Background = Brushes.Transparent;
        BtnMove.Background = Brushes.Transparent;
    }

    private void DeselectTool()
    {
        if (_currentTool is TextTool tt)
            tt.FinalizeActiveTextBox();
        _currentTool = null;
        _currentToolTag = null;
        Cursor = Cursors.Cross;
        DrawingCanvas.Cursor = Cursors.Cross;
        ClearToolHighlights();
    }

    private void ActivateMoveTool()
    {
        DeselectTool();
        Cursor = Cursors.SizeAll;
        DrawingCanvas.Cursor = Cursors.SizeAll;
    }

    private void Eraser_Click(object sender, RoutedEventArgs e)
    {
        PerformUndo();
        Focus();
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (_hasSelection) ActivateMoveTool();
        // Highlight the move button with its icon color (light blue)
        if (sender is Button btn)
            btn.Background = new SolidColorBrush(Color.FromArgb(80, 0x42, 0xA5, 0xF5));
        Focus();
    }

    private void Drawing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasSelection) return;
        var pos = e.GetPosition(DrawingCanvas);
        if (!_selection.Contains(pos)) return;

        // Double-click: toggle between full region and original selection
        if (e.ClickCount == 2)
        {
            ToggleFullRegion();
            e.Handled = true;
            return;
        }

        // OCR sub-selection mode
        if (_ocrMode)
        {
            _interaction = Interaction.OcrSelecting;
            _ocrStart = pos;
            _ocrRect = new System.Windows.Shapes.Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                StrokeThickness = 2,
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(30, 33, 150, 243))
            };
            Canvas.SetLeft(_ocrRect, pos.X);
            Canvas.SetTop(_ocrRect, pos.Y);
            DrawingCanvas.Children.Add(_ocrRect);
            DrawingCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check resize handle (handles sit on selection edge, inside the DrawingCanvas clip)
        var handlePos = e.GetPosition(MainCanvas);
        var handle = HitTestHandle(handlePos);
        if (handle != Handle.None)
        {
            _interaction = Interaction.Resizing;
            _activeHandle = handle;
            _dragStart = handlePos;
            _dragOrigSelection = _selection;
            MainCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Space held OR no tool (V move mode) = pan
        if (_spaceHeld || _currentTool == null)
        {
            _interaction = Interaction.Moving;
            _dragStart = e.GetPosition(MainCanvas);
            _dragOrigSelection = _selection;
            MainCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        // Object eraser: hit-test and remove clicked annotation
        if (_currentTool is EraserTool)
        {
            EraseElementAt(pos);
            e.Handled = true;
            return;
        }

        _interaction = Interaction.Drawing;
        _currentTool.OnMouseDown(pos, DrawingCanvas);
        DrawingCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Drawing_MouseMove(object sender, MouseEventArgs e)
    {
        // OCR sub-selection drag
        if (_interaction == Interaction.OcrSelecting && _ocrRect != null)
        {
            var pos = e.GetPosition(DrawingCanvas);
            var x = Math.Min(_ocrStart.X, pos.X);
            var y = Math.Min(_ocrStart.Y, pos.Y);
            var w = Math.Abs(pos.X - _ocrStart.X);
            var h = Math.Abs(pos.Y - _ocrStart.Y);
            Canvas.SetLeft(_ocrRect, x);
            Canvas.SetTop(_ocrRect, y);
            _ocrRect.Width = w;
            _ocrRect.Height = h;
            e.Handled = true;
            return;
        }

        if (_interaction != Interaction.Drawing || _currentTool == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        _currentTool.OnMouseMove(e.GetPosition(DrawingCanvas), DrawingCanvas);
        e.Handled = true;
    }

    private void Drawing_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // OCR sub-selection complete
        if (_interaction == Interaction.OcrSelecting && _ocrRect != null)
        {
            _interaction = Interaction.None;
            DrawingCanvas.ReleaseMouseCapture();

            var pos = e.GetPosition(DrawingCanvas);
            var x = Math.Min(_ocrStart.X, pos.X);
            var y = Math.Min(_ocrStart.Y, pos.Y);
            var w = Math.Abs(pos.X - _ocrStart.X);
            var h = Math.Abs(pos.Y - _ocrStart.Y);

            // Remove the visual rectangle
            DrawingCanvas.Children.Remove(_ocrRect);
            _ocrRect = null;
            _ocrMode = false;

            if (w > 10 && h > 10)
                PerformOcr(new Rect(x, y, w, h));

            e.Handled = true;
            return;
        }

        if (_interaction != Interaction.Drawing || _currentTool == null) return;
        _interaction = Interaction.None;

        _currentTool.OnMouseUp(e.GetPosition(DrawingCanvas), DrawingCanvas);
        DrawingCanvas.ReleaseMouseCapture();

        if (_currentTool.CurrentAction?.RenderedElement != null)
        {
            _undoStack.Push(_currentTool.CurrentAction);
            _redoStack.Clear();
        }

        e.Handled = true;
        Focus(); // Restore keyboard focus for shortcuts
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;
        if (ColorPaletteCanvas.Visibility == Visibility.Visible)
        {
            ColorPaletteCanvas.Visibility = Visibility.Collapsed;
            return;
        }
        var btnPos = BtnColor.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(ColorPalette, btnPos.X + 40);
        Canvas.SetTop(ColorPalette, btnPos.Y);
        ColorPaletteCanvas.Visibility = Visibility.Visible;
    }

    private void Thickness_Click(object sender, RoutedEventArgs e)
    {
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        if (ThicknessPopupCanvas.Visibility == Visibility.Visible)
        {
            ThicknessPopupCanvas.Visibility = Visibility.Collapsed;
            return;
        }
        var btnPos = BtnThickness.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(ThicknessPopup, btnPos.X + 40);
        Canvas.SetTop(ThicknessPopup, btnPos.Y);
        ThicknessPopupCanvas.Visibility = Visibility.Visible;
    }

    private void ThicknessUp_Click(object? sender, RoutedEventArgs e)
    {
        _currentThickness = Math.Min(10, _currentThickness + 1);
        ThicknessLabel.Text = ((int)_currentThickness).ToString();
        if (_currentTool != null) _currentTool.Thickness = _currentThickness;
    }

    private void ThicknessDown_Click(object? sender, RoutedEventArgs e)
    {
        _currentThickness = Math.Max(1, _currentThickness - 1);
        ThicknessLabel.Text = ((int)_currentThickness).ToString();
        if (_currentTool != null) _currentTool.Thickness = _currentThickness;
    }

    private void PerformUndo()
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack.Pop();

        if (action.ToolType == Models.DrawingToolType.Eraser && action.ErasedElement != null)
        {
            // Undo an erase = restore the element
            DrawingCanvas.Children.Add(action.ErasedElement);
            _redoStack.Push(action);
        }
        else if (action.RenderedElement != null)
        {
            DrawingCanvas.Children.Remove(action.RenderedElement);
            _redoStack.Push(action);
        }
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack.Pop();

        if (action.ToolType == Models.DrawingToolType.Eraser && action.ErasedElement != null)
        {
            // Redo an erase = remove the element again
            DrawingCanvas.Children.Remove(action.ErasedElement);
            _undoStack.Push(action);
        }
        else if (action.RenderedElement != null)
        {
            DrawingCanvas.Children.Add(action.RenderedElement);
            _undoStack.Push(action);
        }
    }

    // ============ OBJECT ERASER ============

    private void EraseElementAt(Point pos)
    {
        // Hit-test with a small area for easier clicking on thin elements
        UIElement? hitChild = null;
        var hitArea = new EllipseGeometry(pos, 6, 6);
        var hitParams = new GeometryHitTestParameters(hitArea);

        VisualTreeHelper.HitTest(DrawingCanvas, null,
            result =>
            {
                var found = GetDrawingCanvasChild(result.VisualHit);
                if (found != null)
                {
                    hitChild = found;
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            },
            hitParams);

        if (hitChild == null) return;

        // Find and remove the matching DrawingAction from the undo stack
        var remaining = new Stack<DrawingAction>();
        DrawingAction? erased = null;

        while (_undoStack.Count > 0)
        {
            var action = _undoStack.Pop();
            if (erased == null && action.RenderedElement == hitChild)
            {
                erased = action;
            }
            else
            {
                remaining.Push(action);
            }
        }

        // Rebuild undo stack without the erased action
        while (remaining.Count > 0)
            _undoStack.Push(remaining.Pop());

        // Remove from canvas
        DrawingCanvas.Children.Remove(hitChild);

        // Push an erase action so it can be undone
        _undoStack.Push(new DrawingAction
        {
            ToolType = Models.DrawingToolType.Eraser,
            ErasedElement = hitChild
        });
        _redoStack.Clear();
    }

    private UIElement? GetDrawingCanvasChild(DependencyObject? visual)
    {
        while (visual != null && visual != DrawingCanvas)
        {
            var parent = VisualTreeHelper.GetParent(visual);
            if (parent == DrawingCanvas)
                return visual as UIElement;
            visual = parent;
        }
        return null;
    }

    // ============ ACTIONS ============

    private BitmapSource RenderFinalImage()
    {
        if (_screenshot == null) return new BitmapImage();

        var cropRect = new Int32Rect(
            (int)_selection.X, (int)_selection.Y,
            (int)_selection.Width, (int)_selection.Height);
        var cropped = ScreenCapture.CropBitmap(_screenshot, cropRect);

        if (DrawingCanvas.Children.Count == 0)
            return cropped;

        var width = (int)_selection.Width;
        var height = (int)_selection.Height;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(cropped, new Rect(0, 0, width, height));
            var brush = new VisualBrush(DrawingCanvas)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = _selection,
                Stretch = Stretch.None
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        var settings = AppSettings.Instance;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
            DefaultExt = settings.DefaultSaveFormat.ToLower(),
            InitialDirectory = settings.LastSaveDirectory,
            FileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        dialog.FilterIndex = settings.DefaultSaveFormat.ToUpper() switch
        {
            "JPG" or "JPEG" => 2, "BMP" => 3, _ => 1
        };

        Hide();
        if (dialog.ShowDialog() == true)
        {
            BitmapEncoder encoder = System.IO.Path.GetExtension(dialog.FileName).ToLower() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = settings.JpegQuality },
                ".bmp" => new BmpBitmapEncoder(),
                _ => new PngBitmapEncoder()
            };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var fs = File.Create(dialog.FileName);
            encoder.Save(fs);

            settings.LastSaveDirectory = System.IO.Path.GetDirectoryName(dialog.FileName) ?? settings.LastSaveDirectory;
            AppSettings.Save();
            HistoryManager.AddRecord(image, dialog.FileName);
        }
        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        Clipboard.SetImage(image);
        HistoryManager.AddClipRecord(image);
        Close();
    }

    private void Ocr_Click(object sender, RoutedEventArgs e)
    {
        // Enter OCR sub-selection mode: user drags a rectangle to pick text area
        if (_currentTool is TextTool tt) tt.FinalizeActiveTextBox();
        DeselectTool();
        _ocrMode = true;
        Cursor = Cursors.Cross;
        DrawingCanvas.Cursor = Cursors.Cross;
        Focus();
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            return source.CompositionTarget.TransformToDevice.M11;
        return 1.0;
    }

    private async void PerformOcr(Rect ocrRegion)
    {
        if (_screenshot == null) return;

        // Scale WPF DIPs to physical pixels (handles DPI scaling)
        double dpi = GetDpiScale();
        int x = Math.Max(0, (int)(ocrRegion.X * dpi));
        int y = Math.Max(0, (int)(ocrRegion.Y * dpi));
        int w = (int)(ocrRegion.Width * dpi);
        int h = (int)(ocrRegion.Height * dpi);

        // Clamp to screenshot bounds
        w = Math.Min(w, _screenshot.PixelWidth - x);
        h = Math.Min(h, _screenshot.PixelHeight - y);

        if (w < 5 || h < 5) return;

        var region = new Int32Rect(x, y, w, h);
        var cropped = ScreenCapture.CropBitmap(_screenshot, region);

        try
        {
            var text = await Core.OcrHelper.ExtractTextAsync(cropped);
            if (string.IsNullOrWhiteSpace(text))
                text = "[No text detected in the selected area]";

            var win = new OcrResultWindow(text) { Topmost = true };
            win.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"OCR failed: {ex.Message}", "Llamashot",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        var win = new HistoryWindow { Topmost = true };
        win.Show();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        new PinWindow(image, _selection).Show();
        Close();
    }

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        var dpi = GetDpiScale();
        int px = (int)(_selection.X * dpi);
        int py = (int)(_selection.Y * dpi);
        int pw = (int)(_selection.Width * dpi);
        int ph = (int)(_selection.Height * dpi);

        // DIP coordinates for the border overlay
        double dx = _selection.X + Left;
        double dy = _selection.Y + Top;
        double dw = _selection.Width;
        double dh = _selection.Height;

        Hide();

        var overlay = new RecordingOverlay(px, py, pw, ph, dx, dy, dw, dh);
        overlay.Show();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ============ KEYBOARD ============

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Skip shortcuts only when actively typing in a text annotation on the canvas
        if (Keyboard.FocusedElement is TextBox tb && DrawingCanvas.IsAncestorOf(tb))
        {
            if (e.Key == Key.Escape)
            {
                Focus();
                e.Handled = true;
            }
            return;
        }

        // Snipping toolbar shortcuts (only when toolbar is visible, before selection)
        if (_interaction == Interaction.ToolbarIdle)
        {
            var st = AppSettings.Instance;
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (ShortcutHelper.Matches(e, st.ShortcutModeScreenshot)) { ModeScreenshot_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (ShortcutHelper.Matches(e, st.ShortcutModeVideo)) { ModeVideo_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (ShortcutHelper.Matches(e, st.ShortcutModeOcr)) { ModeOcr_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (ShortcutHelper.Matches(e, st.ShortcutModeScroll)) { ModeScroll_Click(this, new RoutedEventArgs()); e.Handled = true; return; }
            return;
        }

        // Space = pan mode (Photoshop-style)
        if (e.Key == Key.Space && !_spaceHeld && _hasSelection)
        {
            _spaceHeld = true;
            Cursor = Cursors.Hand;
            DrawingCanvas.Cursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            // Double-Esc: force close immediately
            var now = DateTime.Now;
            if ((now - _lastEscTime).TotalMilliseconds < 500)
            {
                Close();
                e.Handled = true;
                return;
            }
            _lastEscTime = now;

            if (_ocrMode)
            {
                _ocrMode = false;
                if (_ocrRect != null)
                {
                    DrawingCanvas.Children.Remove(_ocrRect);
                    _ocrRect = null;
                }
                Cursor = Cursors.Cross;
                DrawingCanvas.Cursor = Cursors.Cross;
            }
            else if (_currentTool != null)
                DeselectTool();
            else
                Close();
            e.Handled = true;
            return;
        }

        var s = AppSettings.Instance;

        // Action shortcuts
        if (ShortcutHelper.Matches(e, s.ShortcutSave))
        { if (_hasSelection) Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutCopy))
        { if (_hasSelection) Copy_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutUndo))
        { PerformUndo(); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutRedo))
        { PerformRedo(); e.Handled = true; }
        // Tool shortcuts
        else if (ShortcutHelper.Matches(e, s.ShortcutPen))
        { SelectToolByTag("Pen"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutLine))
        { SelectToolByTag("Line"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutArrow))
        { SelectToolByTag("Arrow"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutRectangle))
        { SelectToolByTag("Rectangle"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutEllipse))
        { SelectToolByTag("Ellipse"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutText))
        { SelectToolByTag("Text"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutMarker))
        { SelectToolByTag("Marker"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutBlur))
        { SelectToolByTag("Blur"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutCheck))
        { SelectToolByTag("Check"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutCross))
        { SelectToolByTag("CrossMark"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutObjectEraser))
        { SelectToolByTag("Eraser"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutEraser))
        { Eraser_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutMove))
        { if (_hasSelection) { ActivateMoveTool(); BtnMove.Background = new SolidColorBrush(Color.FromArgb(80, 0x42, 0xA5, 0xF5)); } e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutColor))
        { if (_hasSelection) Color_Click(BtnColor, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutThickness))
        { if (_hasSelection) Thickness_Click(BtnThickness, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutHistory))
        { if (_hasSelection) History_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutRecord))
        { if (_hasSelection) Record_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutOcr))
        { if (_hasSelection) Ocr_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutPin))
        { if (_hasSelection) Pin_Click(this, new RoutedEventArgs()); e.Handled = true; }
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _spaceHeld)
        {
            _spaceHeld = false;
            Cursor = Cursors.Cross;
            DrawingCanvas.Cursor = _currentTool?.Cursor ?? Cursors.Cross;
            e.Handled = true;
        }
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentTool != null)
        {
            if (e.Delta > 0) ThicknessUp_Click(this, new RoutedEventArgs());
            else ThicknessDown_Click(this, new RoutedEventArgs());
        }
    }

    private void SelectToolByTag(string tag)
    {
        if (!_hasSelection) return;
        foreach (var child in ToolWrapPanel.Children)
            if (child is Button btn && btn.Tag is string t && t == tag)
            { Tool_Click(btn, new RoutedEventArgs()); return; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _screenshot = null;
        DrawingCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        base.OnClosed(e);
    }
}

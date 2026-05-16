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

public partial class ScrollPreviewWindow : Window
{
    private readonly BitmapSource _image;
    private IDrawingTool? _currentTool;
    private string? _currentToolTag;
    private readonly Stack<DrawingAction> _undoStack = new();
    private readonly Stack<DrawingAction> _redoStack = new();
    private Color _currentColor = Colors.Yellow;
    private double _currentThickness = 3;
    private bool _isDrawing;

    // Crop state
    private bool _cropMode;
    private bool _cropDragging;
    private Point _cropStart;
    private Rect _cropRect;
    private Rect _cropDragOrigRect;
    private enum CropHandle { None, TL, T, TR, R, BR, B, BL, L }
    private CropHandle _activeCropHandle = CropHandle.None;
    private const double CropHandleSize = 10;
    private const double CropHitMargin = 8;

    private static readonly Dictionary<string, Color> ToolColors = new()
    {
        { "Pen", Color.FromRgb(0xFF, 0xA7, 0x26) },
        { "Line", Color.FromRgb(0x64, 0xB5, 0xF6) },
        { "Arrow", Color.FromRgb(0x26, 0xC6, 0xDA) },
        { "Rectangle", Color.FromRgb(0x42, 0xA5, 0xF5) },
        { "Ellipse", Color.FromRgb(0xAB, 0x47, 0xBC) },
        { "Text", Color.FromRgb(0xFF, 0xCA, 0x28) },
        { "Marker", Color.FromRgb(0xFF, 0xEE, 0x58) },
        { "Blur", Color.FromRgb(0x78, 0x90, 0x9C) },
        { "Check", Color.FromRgb(0x4C, 0xAF, 0x50) },
        { "CrossMark", Color.FromRgb(0xF4, 0x43, 0x36) },
        { "Eraser", Color.FromRgb(0xEF, 0x53, 0x50) },
    };

    private static readonly Color[] PaletteColors = {
        Colors.Red, Colors.OrangeRed, Colors.Orange, Colors.Gold,
        Colors.Yellow, Colors.GreenYellow, Colors.LimeGreen, Colors.Green,
        Colors.Teal, Colors.DodgerBlue, Colors.Blue, Colors.DarkBlue,
        Colors.Purple, Colors.Magenta, Colors.DeepPink, Colors.HotPink,
        Colors.White, Colors.LightGray, Colors.Gray, Colors.DarkGray,
        Colors.Black, Colors.Brown, Colors.SaddleBrown, Colors.Maroon
    };

    public ScrollPreviewWindow(BitmapSource image)
    {
        InitializeComponent();
        _image = image;

        PreviewImage.Source = image;
        PreviewImage.Width = image.PixelWidth;
        PreviewImage.Height = image.PixelHeight;
        DrawingCanvas.Width = image.PixelWidth;
        DrawingCanvas.Height = image.PixelHeight;
        CropCanvas.Width = image.PixelWidth;
        CropCanvas.Height = image.PixelHeight;

        TxtInfo.Text = $"{image.PixelWidth} x {image.PixelHeight}";

        // Load persisted color
        try
        {
            _currentColor = (Color)System.Windows.Media.ColorConverter.ConvertFromString(
                AppSettings.Instance.DefaultColor);
            ColorIndicator.Fill = new SolidColorBrush(_currentColor);
        }
        catch { }

        ThicknessLabel.Text = "3";
        InitializeColorPalette();
        InitializeThicknessPopup();

        // Set initial crop to full image
        _cropRect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
    }

    // ============ DRAWING TOOLS ============

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string toolName) return;
        ExitCropMode();
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;

        if (_currentTool is TextTool tt) tt.FinalizeActiveTextBox();

        // Toggle
        if (_currentToolTag == toolName)
        {
            DeselectTool();
            return;
        }

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
            "Blur" => new BlurTool { ScreenshotSource = _image },
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
    }

    private void DeselectTool()
    {
        if (_currentTool is TextTool tt) tt.FinalizeActiveTextBox();
        _currentTool = null;
        _currentToolTag = null;
        DrawingCanvas.Cursor = Cursors.Arrow;
        ClearToolHighlights();
    }

    private void ClearToolHighlights()
    {
        foreach (var child in ToolWrapPanel.Children)
            if (child is Button b && b.Tag is string) b.Background = Brushes.Transparent;
    }

    private void Drawing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == null || _cropMode) return;
        var pos = e.GetPosition(DrawingCanvas);

        if (_currentTool is EraserTool)
        {
            EraseElementAt(pos);
            e.Handled = true;
            return;
        }

        _isDrawing = true;
        _currentTool.OnMouseDown(pos, DrawingCanvas);
        DrawingCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Drawing_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _currentTool == null) return;
        _currentTool.OnMouseMove(e.GetPosition(DrawingCanvas), DrawingCanvas);
        e.Handled = true;
    }

    private void Drawing_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing || _currentTool == null) return;
        _isDrawing = false;

        _currentTool.OnMouseUp(e.GetPosition(DrawingCanvas), DrawingCanvas);
        DrawingCanvas.ReleaseMouseCapture();

        if (_currentTool.CurrentAction?.RenderedElement != null)
        {
            _undoStack.Push(_currentTool.CurrentAction);
            _redoStack.Clear();
        }
        e.Handled = true;
    }

    private void EraseElementAt(Point pos)
    {
        UIElement? hitChild = null;
        var hitArea = new EllipseGeometry(pos, 6, 6);
        var hitParams = new GeometryHitTestParameters(hitArea);

        VisualTreeHelper.HitTest(DrawingCanvas, null,
            result =>
            {
                var found = GetDrawingCanvasChild(result.VisualHit);
                if (found != null) { hitChild = found; return HitTestResultBehavior.Stop; }
                return HitTestResultBehavior.Continue;
            }, hitParams);

        if (hitChild == null) return;

        var remaining = new Stack<DrawingAction>();
        DrawingAction? erased = null;
        while (_undoStack.Count > 0)
        {
            var action = _undoStack.Pop();
            if (erased == null && action.RenderedElement == hitChild) erased = action;
            else remaining.Push(action);
        }
        while (remaining.Count > 0) _undoStack.Push(remaining.Pop());

        DrawingCanvas.Children.Remove(hitChild);
        _undoStack.Push(new DrawingAction { ToolType = DrawingToolType.Eraser, ErasedElement = hitChild });
        _redoStack.Clear();
    }

    private UIElement? GetDrawingCanvasChild(DependencyObject? visual)
    {
        while (visual != null && visual != DrawingCanvas)
        {
            var parent = VisualTreeHelper.GetParent(visual);
            if (parent == DrawingCanvas) return visual as UIElement;
            visual = parent;
        }
        return null;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack.Pop();
        if (action.ToolType == DrawingToolType.Eraser && action.ErasedElement != null)
        {
            DrawingCanvas.Children.Add(action.ErasedElement);
            _redoStack.Push(action);
        }
        else if (action.RenderedElement != null)
        {
            DrawingCanvas.Children.Remove(action.RenderedElement);
            _redoStack.Push(action);
        }
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack.Pop();
        if (action.ToolType == DrawingToolType.Eraser && action.ErasedElement != null)
        {
            DrawingCanvas.Children.Remove(action.ErasedElement);
            _undoStack.Push(action);
        }
        else if (action.RenderedElement != null)
        {
            DrawingCanvas.Children.Add(action.RenderedElement);
            _undoStack.Push(action);
        }
    }

    // ============ COLOR & THICKNESS ============

    private void InitializeColorPalette()
    {
        foreach (var color in PaletteColors)
        {
            var swatch = new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (s, e) =>
            {
                _currentColor = color;
                ColorIndicator.Fill = new SolidColorBrush(color);
                if (_currentTool != null) _currentTool.StrokeColor = color;
                ColorPaletteCanvas.Visibility = Visibility.Collapsed;
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
                        new Ellipse
                        {
                            Width = Math.Max(4, val * 1.6), Height = Math.Max(4, val * 1.6),
                            Fill = Brushes.White, Margin = new Thickness(2, 0, 4, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = val.ToString(), Foreground = Brushes.White,
                            FontSize = 12, VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand, Focusable = false, Margin = new Thickness(1)
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

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = ColorPaletteCanvas.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        if (ColorPaletteCanvas.Visibility == Visibility.Visible)
        {
            Canvas.SetLeft(ColorPalette, 10);
            Canvas.SetTop(ColorPalette, 10);
        }
    }

    private void Thickness_Click(object sender, RoutedEventArgs e)
    {
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        ThicknessPopupCanvas.Visibility = ThicknessPopupCanvas.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
        if (ThicknessPopupCanvas.Visibility == Visibility.Visible)
        {
            Canvas.SetLeft(ThicknessPopup, 10);
            Canvas.SetTop(ThicknessPopup, 10);
        }
    }

    // ============ CROP ============

    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        if (_cropMode) { ExitCropMode(); return; }

        DeselectTool();
        _cropMode = true;
        BtnCrop.Background = new SolidColorBrush(Color.FromArgb(80, 0x66, 0xBB, 0x6A));
        CropCanvas.Visibility = Visibility.Visible;
        DrawingCanvas.Visibility = Visibility.Collapsed;

        _cropRect = new Rect(0, 0, _image.PixelWidth, _image.PixelHeight);
        UpdateCropVisuals();
    }

    private void ExitCropMode()
    {
        _cropMode = false;
        _cropDragging = false;
        _activeCropHandle = CropHandle.None;
        CropCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Visible;
        BtnCrop.Background = Brushes.Transparent;
    }

    private void Crop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_cropMode) return;
        var pos = e.GetPosition(CropCanvas);

        _activeCropHandle = HitTestCropHandle(pos);
        if (_activeCropHandle != CropHandle.None)
        {
            _cropDragging = true;
            _cropStart = pos;
            _cropDragOrigRect = _cropRect;
            CropCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Crop_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(CropCanvas);

        if (_cropDragging && _activeCropHandle != CropHandle.None)
        {
            double dx = pos.X - _cropStart.X;
            double dy = pos.Y - _cropStart.Y;
            var r = _cropDragOrigRect;
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

            switch (_activeCropHandle)
            {
                case CropHandle.TL: left += dx; top += dy; break;
                case CropHandle.T: top += dy; break;
                case CropHandle.TR: right += dx; top += dy; break;
                case CropHandle.R: right += dx; break;
                case CropHandle.BR: right += dx; bottom += dy; break;
                case CropHandle.B: bottom += dy; break;
                case CropHandle.BL: left += dx; bottom += dy; break;
                case CropHandle.L: left += dx; break;
            }

            left = Math.Clamp(left, 0, _image.PixelWidth - 10);
            top = Math.Clamp(top, 0, _image.PixelHeight - 10);
            right = Math.Clamp(right, left + 10, _image.PixelWidth);
            bottom = Math.Clamp(bottom, top + 10, _image.PixelHeight);

            _cropRect = new Rect(left, top, right - left, bottom - top);
            UpdateCropVisuals();
            e.Handled = true;
        }
        else if (_cropMode)
        {
            // Update cursor based on handle hover
            var handle = HitTestCropHandle(pos);
            CropCanvas.Cursor = handle switch
            {
                CropHandle.TL or CropHandle.BR => Cursors.SizeNWSE,
                CropHandle.TR or CropHandle.BL => Cursors.SizeNESW,
                CropHandle.T or CropHandle.B => Cursors.SizeNS,
                CropHandle.L or CropHandle.R => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
        }
    }

    private void Crop_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_cropDragging) return;
        _cropDragging = false;
        _activeCropHandle = CropHandle.None;
        CropCanvas.ReleaseMouseCapture();
        UpdateCropVisuals();
    }

    private CropHandle HitTestCropHandle(Point pos)
    {
        var r = _cropRect;
        double m = CropHitMargin;
        var handles = new (CropHandle h, double x, double y)[]
        {
            (CropHandle.TL, r.Left, r.Top),
            (CropHandle.T, r.Left + r.Width / 2, r.Top),
            (CropHandle.TR, r.Right, r.Top),
            (CropHandle.R, r.Right, r.Top + r.Height / 2),
            (CropHandle.BR, r.Right, r.Bottom),
            (CropHandle.B, r.Left + r.Width / 2, r.Bottom),
            (CropHandle.BL, r.Left, r.Bottom),
            (CropHandle.L, r.Left, r.Top + r.Height / 2),
        };
        foreach (var (h, hx, hy) in handles)
        {
            if (new Rect(hx - m, hy - m, m * 2, m * 2).Contains(pos))
                return h;
        }
        return CropHandle.None;
    }

    private void UpdateCropVisuals()
    {
        Canvas.SetLeft(CropRect, _cropRect.X);
        Canvas.SetTop(CropRect, _cropRect.Y);
        CropRect.Width = _cropRect.Width;
        CropRect.Height = _cropRect.Height;

        // Dimming outside crop
        var full = new RectangleGeometry(new Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
        var hole = new RectangleGeometry(_cropRect);
        CropDimming.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, hole);

        // Draw handles
        CropHandleCanvas.Children.Clear();
        var r = _cropRect;
        var pts = new (double x, double y)[]
        {
            (r.Left, r.Top), (r.Left + r.Width / 2, r.Top), (r.Right, r.Top),
            (r.Right, r.Top + r.Height / 2), (r.Right, r.Bottom),
            (r.Left + r.Width / 2, r.Bottom), (r.Left, r.Bottom),
            (r.Left, r.Top + r.Height / 2),
        };
        foreach (var (hx, hy) in pts)
        {
            var rect = new Rectangle
            {
                Width = CropHandleSize, Height = CropHandleSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)),
                StrokeThickness = 1.5,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, hx - CropHandleSize / 2);
            Canvas.SetTop(rect, hy - CropHandleSize / 2);
            CropHandleCanvas.Children.Add(rect);
        }
    }

    // ============ RENDER FINAL IMAGE ============

    private BitmapSource RenderFinalImage()
    {
        int width = _image.PixelWidth;
        int height = _image.PixelHeight;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_image, new Rect(0, 0, width, height));

            if (DrawingCanvas.Children.Count > 0)
            {
                var brush = new VisualBrush(DrawingCanvas)
                {
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, width, height),
                    Stretch = Stretch.None
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        // Apply crop if not full image
        if (_cropRect.Width > 0 && _cropRect.Height > 0 &&
            (_cropRect.X > 0 || _cropRect.Y > 0 ||
             _cropRect.Width < width || _cropRect.Height < height))
        {
            var cropped = ScreenCapture.CropBitmap(rtb, new Int32Rect(
                (int)_cropRect.X, (int)_cropRect.Y,
                (int)_cropRect.Width, (int)_cropRect.Height));
            return cropped;
        }

        rtb.Freeze();
        return rtb;
    }

    // ============ ACTIONS ============

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        var settings = AppSettings.Instance;

        var dialog = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
            DefaultExt = settings.DefaultSaveFormat.ToLower(),
            InitialDirectory = settings.LastSaveDirectory,
            FileName = $"scroll_{DateTime.Now:yyyyMMdd_HHmmss}"
        };
        dialog.FilterIndex = settings.DefaultSaveFormat.ToUpper() switch
        {
            "JPG" or "JPEG" => 2, "BMP" => 3, _ => 1
        };

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

    private void Discard_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Skip shortcuts when typing in text annotation
        if (Keyboard.FocusedElement is TextBox tb && DrawingCanvas.IsAncestorOf(tb))
        {
            if (e.Key == Key.Escape) { Focus(); e.Handled = true; }
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_cropMode) { ExitCropMode(); e.Handled = true; }
            else if (_currentTool != null) { DeselectTool(); e.Handled = true; }
            else { Close(); e.Handled = true; }
            return;
        }

        var s = AppSettings.Instance;

        // Actions
        if (ShortcutHelper.Matches(e, s.ShortcutSave)) { Save_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutCopy)) { Copy_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutUndo)) { Undo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutRedo)) { Redo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        // Tool shortcuts (toggle: same shortcut again deselects)
        else if (ShortcutHelper.Matches(e, s.ShortcutPen)) { SelectToolByTag("Pen"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutLine)) { SelectToolByTag("Line"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutArrow)) { SelectToolByTag("Arrow"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutRectangle)) { SelectToolByTag("Rectangle"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutEllipse)) { SelectToolByTag("Ellipse"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutText)) { SelectToolByTag("Text"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutMarker)) { SelectToolByTag("Marker"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutBlur)) { SelectToolByTag("Blur"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutCheck)) { SelectToolByTag("Check"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutCross)) { SelectToolByTag("CrossMark"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutObjectEraser)) { SelectToolByTag("Eraser"); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutEraser)) { Undo_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutColor)) { Color_Click(BtnColor, new RoutedEventArgs()); e.Handled = true; }
        else if (ShortcutHelper.Matches(e, s.ShortcutThickness)) { Thickness_Click(BtnThickness, new RoutedEventArgs()); e.Handled = true; }

        base.OnKeyDown(e);
    }

    private void SelectToolByTag(string tag)
    {
        foreach (var child in ToolWrapPanel.Children)
            if (child is Button btn && btn.Tag is string t && t == tag)
            { Tool_Click(btn, new RoutedEventArgs()); return; }
    }
}

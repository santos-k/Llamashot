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
    // Screenshot
    private BitmapSource? _screenshot;
    private Rect _virtualBounds;

    // Selection state
    private enum Mode { Selecting, Selected, Drawing }
    private Mode _mode = Mode.Selecting;
    private Point _selStart;
    private Point _selEnd;
    private Rect _selection;
    private bool _isDragging;

    // Resize
    private enum ResizeHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Move }
    private ResizeHandle _activeHandle = ResizeHandle.None;
    private Point _resizeStart;
    private Rect _resizeOriginal;

    // Drawing
    private IDrawingTool? _currentTool;
    private readonly Stack<DrawingAction> _undoStack = new();
    private readonly Stack<DrawingAction> _redoStack = new();
    private Color _currentColor = Colors.Red;
    private double _currentThickness = 2;

    // Color palette
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
    }

    public void StartCapture()
    {
        _virtualBounds = ScreenCapture.GetVirtualScreenBounds();
        _screenshot = ScreenCapture.CaptureFullScreen();

        ScreenshotImage.Source = _screenshot;

        // Position window to cover all screens
        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;

        // Set initial full dimming
        UpdateDimming(Rect.Empty);

        _mode = Mode.Selecting;
        _isDragging = false;
        SelectionBorder.Visibility = Visibility.Collapsed;
        DimensionBorder.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();

        Show();
        Activate();
    }

    private void InitializeColorPalette()
    {
        foreach (var color in PaletteColors)
        {
            var swatch = new Border
            {
                Width = 22, Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (s, e) =>
            {
                _currentColor = color;
                ColorIndicator.Fill = new SolidColorBrush(color);
                if (_currentTool != null) _currentTool.StrokeColor = color;
                ColorPaletteCanvas.Visibility = Visibility.Collapsed;
                e.Handled = true;
            };
            ColorSwatches.Children.Add(swatch);
        }
    }

    // ============ SELECTION ============

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == Mode.Drawing) return;

        var pos = e.GetPosition(MainCanvas);

        if (_mode == Mode.Selected)
        {
            // Check if clicking on a resize handle
            var handle = HitTestHandle(pos);
            if (handle != ResizeHandle.None)
            {
                _activeHandle = handle;
                _resizeStart = pos;
                _resizeOriginal = _selection;
                _isDragging = true;
                MainCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Check if clicking inside selection (move)
            if (_selection.Contains(pos))
            {
                _activeHandle = ResizeHandle.Move;
                _resizeStart = pos;
                _resizeOriginal = _selection;
                _isDragging = true;
                MainCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // Clicking outside - start new selection
            HideToolbars();
        }

        _mode = Mode.Selecting;
        _selStart = pos;
        _selEnd = pos;
        _isDragging = true;
        MainCanvas.CaptureMouse();

        SelectionBorder.Visibility = Visibility.Visible;
        DimensionBorder.Visibility = Visibility.Visible;
        UpdateSelection();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(MainCanvas);

        if (!_isDragging)
        {
            if (_mode == Mode.Selected)
            {
                var handle = HitTestHandle(pos);
                Cursor = handle switch
                {
                    ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
                    ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
                    ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
                    ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
                    ResizeHandle.Move when _selection.Contains(pos) => Cursors.SizeAll,
                    _ => Cursors.Cross
                };
            }
            return;
        }

        if (_mode == Mode.Selecting)
        {
            _selEnd = pos;
            UpdateSelection();
        }
        else if (_mode == Mode.Selected && _activeHandle != ResizeHandle.None)
        {
            ApplyResize(pos);
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        MainCanvas.ReleaseMouseCapture();

        if (_mode == Mode.Selecting)
        {
            _selEnd = e.GetPosition(MainCanvas);
            UpdateSelection();

            if (_selection.Width > 5 && _selection.Height > 5)
            {
                _mode = Mode.Selected;
                ShowToolbars();
                ShowResizeHandles();
            }
        }
        else if (_mode == Mode.Selected)
        {
            _activeHandle = ResizeHandle.None;
            UpdateToolbarPositions();
        }
    }

    private void UpdateSelection()
    {
        double x = Math.Min(_selStart.X, _selEnd.X);
        double y = Math.Min(_selStart.Y, _selEnd.Y);
        double w = Math.Abs(_selEnd.X - _selStart.X);
        double h = Math.Abs(_selEnd.Y - _selStart.Y);

        _selection = new Rect(x, y, w, h);

        Canvas.SetLeft(SelectionBorder, x);
        Canvas.SetTop(SelectionBorder, y);
        SelectionBorder.Width = w;
        SelectionBorder.Height = h;
        SelectionBorder.Visibility = Visibility.Visible;

        // Update dimension text
        DimensionText.Text = $"{(int)w} x {(int)h}";
        Canvas.SetLeft(DimensionBorder, x);
        Canvas.SetTop(DimensionBorder, Math.Max(0, y - 25));
        DimensionBorder.Visibility = Visibility.Visible;

        UpdateDimming(_selection);
    }

    private void UpdateDimming(Rect selectionRect)
    {
        var fullRect = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));

        if (selectionRect.Width > 0 && selectionRect.Height > 0)
        {
            var selGeom = new RectangleGeometry(selectionRect);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, selGeom);
            DimmingPath.Data = combined;
        }
        else
        {
            DimmingPath.Data = fullRect;
        }
    }

    // ============ RESIZE HANDLES ============

    private const double HandleSize = 8;

    private void ShowResizeHandles()
    {
        HandleCanvas.Children.Clear();
        HandleCanvas.Visibility = Visibility.Visible;

        var positions = new (ResizeHandle, double, double)[]
        {
            (ResizeHandle.TopLeft, _selection.Left, _selection.Top),
            (ResizeHandle.Top, _selection.Left + _selection.Width / 2, _selection.Top),
            (ResizeHandle.TopRight, _selection.Right, _selection.Top),
            (ResizeHandle.Right, _selection.Right, _selection.Top + _selection.Height / 2),
            (ResizeHandle.BottomRight, _selection.Right, _selection.Bottom),
            (ResizeHandle.Bottom, _selection.Left + _selection.Width / 2, _selection.Bottom),
            (ResizeHandle.BottomLeft, _selection.Left, _selection.Bottom),
            (ResizeHandle.Left, _selection.Left, _selection.Top + _selection.Height / 2),
        };

        foreach (var (handle, cx, cy) in positions)
        {
            var rect = new Rectangle
            {
                Width = HandleSize, Height = HandleSize,
                Fill = new SolidColorBrush(Colors.White),
                Stroke = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                StrokeThickness = 1.5,
                Tag = handle,
                Cursor = handle switch
                {
                    ResizeHandle.TopLeft or ResizeHandle.BottomRight => Cursors.SizeNWSE,
                    ResizeHandle.TopRight or ResizeHandle.BottomLeft => Cursors.SizeNESW,
                    ResizeHandle.Top or ResizeHandle.Bottom => Cursors.SizeNS,
                    ResizeHandle.Left or ResizeHandle.Right => Cursors.SizeWE,
                    _ => Cursors.Arrow
                }
            };

            Canvas.SetLeft(rect, cx - HandleSize / 2);
            Canvas.SetTop(rect, cy - HandleSize / 2);
            HandleCanvas.Children.Add(rect);
        }
    }

    private ResizeHandle HitTestHandle(Point pos)
    {
        foreach (UIElement child in HandleCanvas.Children)
        {
            if (child is Rectangle rect && rect.Tag is ResizeHandle handle)
            {
                double x = Canvas.GetLeft(rect);
                double y = Canvas.GetTop(rect);
                var hitRect = new Rect(x - 4, y - 4, HandleSize + 8, HandleSize + 8);
                if (hitRect.Contains(pos))
                    return handle;
            }
        }

        if (_selection.Contains(pos))
            return ResizeHandle.Move;

        return ResizeHandle.None;
    }

    private void ApplyResize(Point pos)
    {
        var dx = pos.X - _resizeStart.X;
        var dy = pos.Y - _resizeStart.Y;
        var r = _resizeOriginal;

        switch (_activeHandle)
        {
            case ResizeHandle.TopLeft:
                _selection = new Rect(r.Left + dx, r.Top + dy, r.Width - dx, r.Height - dy);
                break;
            case ResizeHandle.Top:
                _selection = new Rect(r.Left, r.Top + dy, r.Width, r.Height - dy);
                break;
            case ResizeHandle.TopRight:
                _selection = new Rect(r.Left, r.Top + dy, r.Width + dx, r.Height - dy);
                break;
            case ResizeHandle.Right:
                _selection = new Rect(r.Left, r.Top, r.Width + dx, r.Height);
                break;
            case ResizeHandle.BottomRight:
                _selection = new Rect(r.Left, r.Top, r.Width + dx, r.Height + dy);
                break;
            case ResizeHandle.Bottom:
                _selection = new Rect(r.Left, r.Top, r.Width, r.Height + dy);
                break;
            case ResizeHandle.BottomLeft:
                _selection = new Rect(r.Left + dx, r.Top, r.Width - dx, r.Height + dy);
                break;
            case ResizeHandle.Left:
                _selection = new Rect(r.Left + dx, r.Top, r.Width - dx, r.Height);
                break;
            case ResizeHandle.Move:
                _selection = new Rect(r.Left + dx, r.Top + dy, r.Width, r.Height);
                break;
        }

        // Normalize (ensure positive width/height)
        if (_selection.Width < 0)
            _selection = new Rect(_selection.Right, _selection.Top, -_selection.Width, _selection.Height);
        if (_selection.Height < 0)
            _selection = new Rect(_selection.Left, _selection.Bottom, _selection.Width, -_selection.Height);

        // Update visuals
        Canvas.SetLeft(SelectionBorder, _selection.Left);
        Canvas.SetTop(SelectionBorder, _selection.Top);
        SelectionBorder.Width = _selection.Width;
        SelectionBorder.Height = _selection.Height;

        DimensionText.Text = $"{(int)_selection.Width} x {(int)_selection.Height}";
        Canvas.SetLeft(DimensionBorder, _selection.Left);
        Canvas.SetTop(DimensionBorder, Math.Max(0, _selection.Top - 25));

        UpdateDimming(_selection);
        ShowResizeHandles();
        UpdateDrawingCanvasClip();
    }

    // ============ TOOLBARS ============

    private void ShowToolbars()
    {
        ToolbarCanvas.Visibility = Visibility.Visible;
        DrawingCanvas.Visibility = Visibility.Visible;
        UpdateToolbarPositions();
        UpdateDrawingCanvasClip();
    }

    private void HideToolbars()
    {
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
    }

    private void UpdateToolbarPositions()
    {
        // Drawing toolbar: right side of selection
        double dtLeft = _selection.Right + 6;
        double dtTop = _selection.Top;

        // If toolbar would go off screen, put it on the left
        DrawingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (dtLeft + DrawingToolbar.DesiredSize.Width > ActualWidth)
            dtLeft = _selection.Left - DrawingToolbar.DesiredSize.Width - 6;

        Canvas.SetLeft(DrawingToolbar, dtLeft);
        Canvas.SetTop(DrawingToolbar, dtTop);

        // Action toolbar: below selection
        double atLeft = _selection.Left;
        double atTop = _selection.Bottom + 6;

        ActionToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (atTop + ActionToolbar.DesiredSize.Height > ActualHeight)
            atTop = _selection.Top - ActionToolbar.DesiredSize.Height - 6;

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

        // Reset all button backgrounds
        foreach (var child in DrawingToolsPanel.Children)
        {
            if (child is Button b && b.Content is Border border)
                border.Background = new SolidColorBrush(Colors.Transparent);
        }

        // Highlight selected tool
        if (btn.Content is Border selectedBorder)
            selectedBorder.Background = new SolidColorBrush(Color.FromArgb(60, 33, 150, 243));

        _currentTool = toolName switch
        {
            "Pen" => new PenTool(),
            "Line" => new LineTool(),
            "Arrow" => new ArrowTool(),
            "Rectangle" => new RectangleTool(),
            "Ellipse" => new EllipseTool(),
            "Text" => new TextTool { FontSize = Math.Max(12, _currentThickness * 8) },
            "Marker" => new MarkerTool(),
            "Blur" => new BlurTool { ScreenshotSource = _screenshot },
            _ => null
        };

        if (_currentTool != null)
        {
            _currentTool.StrokeColor = _currentColor;
            _currentTool.Thickness = _currentThickness;
            _mode = Mode.Drawing;
            DrawingCanvas.Cursor = _currentTool.Cursor;
        }
    }

    private void Drawing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode != Mode.Drawing || _currentTool == null) return;
        var pos = e.GetPosition(DrawingCanvas);
        if (!_selection.Contains(pos)) return;

        _currentTool.OnMouseDown(pos, DrawingCanvas);
        DrawingCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Drawing_MouseMove(object sender, MouseEventArgs e)
    {
        if (_mode != Mode.Drawing || _currentTool == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(DrawingCanvas);
        _currentTool.OnMouseMove(pos, DrawingCanvas);
        e.Handled = true;
    }

    private void Drawing_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode != Mode.Drawing || _currentTool == null) return;
        var pos = e.GetPosition(DrawingCanvas);
        _currentTool.OnMouseUp(pos, DrawingCanvas);
        DrawingCanvas.ReleaseMouseCapture();

        if (_currentTool.CurrentAction?.RenderedElement != null)
        {
            _undoStack.Push(_currentTool.CurrentAction);
            _redoStack.Clear();
        }

        e.Handled = true;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (ColorPaletteCanvas.Visibility == Visibility.Visible)
        {
            ColorPaletteCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        // Position palette near the color button
        var btnPos = BtnColor.TranslatePoint(new Point(0, 0), RootGrid);
        Canvas.SetLeft(ColorPalette, btnPos.X + 40);
        Canvas.SetTop(ColorPalette, btnPos.Y);
        ColorPaletteCanvas.Visibility = Visibility.Visible;
    }

    private void ThicknessUp_Click(object sender, RoutedEventArgs e)
    {
        _currentThickness = Math.Min(20, _currentThickness + 1);
        if (_currentTool != null) _currentTool.Thickness = _currentThickness;
    }

    private void ThicknessDown_Click(object sender, RoutedEventArgs e)
    {
        _currentThickness = Math.Max(1, _currentThickness - 1);
        if (_currentTool != null) _currentTool.Thickness = _currentThickness;
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => PerformUndo();
    private void Redo_Click(object sender, RoutedEventArgs e) => PerformRedo();

    private void PerformUndo()
    {
        if (_undoStack.Count == 0) return;
        var action = _undoStack.Pop();
        if (action.RenderedElement != null)
        {
            DrawingCanvas.Children.Remove(action.RenderedElement);
            _redoStack.Push(action);
        }
    }

    private void PerformRedo()
    {
        if (_redoStack.Count == 0) return;
        var action = _redoStack.Pop();
        if (action.RenderedElement != null)
        {
            DrawingCanvas.Children.Add(action.RenderedElement);
            _undoStack.Push(action);
        }
    }

    // ============ ACTIONS ============

    private BitmapSource RenderFinalImage()
    {
        if (_screenshot == null) return new BitmapImage();

        // Crop screenshot to selection
        var cropRect = new Int32Rect(
            (int)_selection.X, (int)_selection.Y,
            (int)_selection.Width, (int)_selection.Height);

        var cropped = ScreenCapture.CropBitmap(_screenshot, cropRect);

        if (DrawingCanvas.Children.Count == 0)
            return cropped;

        // Render drawing canvas content onto the cropped image
        var dpi = 96.0;
        var width = (int)_selection.Width;
        var height = (int)_selection.Height;

        var drawingVisual = new DrawingVisual();
        using (var dc = drawingVisual.RenderOpen())
        {
            // Draw the cropped screenshot
            dc.DrawImage(cropped, new Rect(0, 0, width, height));

            // Render the drawing canvas
            var canvasSize = new Size(_selection.Width, _selection.Height);

            // We need to translate since DrawingCanvas elements are in screen coordinates
            dc.PushTransform(new TranslateTransform(-_selection.X, -_selection.Y));

            foreach (UIElement child in DrawingCanvas.Children)
            {
                child.Measure(new Size(ActualWidth, ActualHeight));
                child.Arrange(new Rect(0, 0, ActualWidth, ActualHeight));
            }

            // Render the entire drawing canvas
            var canvasBrush = new VisualBrush(DrawingCanvas)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = _selection,
                Stretch = Stretch.None
            };
            dc.Pop();
            dc.DrawRectangle(canvasBrush, null, new Rect(0, 0, width, height));
        }

        var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(drawingVisual);
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

        // Set filter index based on default format
        dialog.FilterIndex = settings.DefaultSaveFormat.ToUpper() switch
        {
            "JPG" or "JPEG" => 2,
            "BMP" => 3,
            _ => 1
        };

        Hide(); // Hide overlay for save dialog

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

            // Add to history
            HistoryManager.AddRecord(image, dialog.FileName);
        }

        Close();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        Clipboard.SetImage(image as BitmapSource ?? new BitmapImage());
        Close();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(image, new Rect(0, 0,
                    printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight));
            }
            printDialog.PrintVisual(visual, "Llamashot Screenshot");
        }

        Close();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        var pinWindow = new PinWindow(image, _selection);
        pinWindow.Show();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ============ KEYBOARD ============

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (_mode == Mode.Drawing)
                {
                    _mode = Mode.Selected;
                    _currentTool = null;
                    DrawingCanvas.Cursor = Cursors.Cross;
                    // Reset tool button highlights
                    foreach (var child in DrawingToolsPanel.Children)
                        if (child is Button b && b.Content is Border border)
                            border.Background = new SolidColorBrush(Colors.Transparent);
                }
                else
                {
                    Close();
                }
                break;

            case Key.S when Keyboard.Modifiers == ModifierKeys.Control:
                if (_mode == Mode.Selected || _mode == Mode.Drawing)
                    Save_Click(this, new RoutedEventArgs());
                break;

            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                if (_mode == Mode.Selected || _mode == Mode.Drawing)
                    Copy_Click(this, new RoutedEventArgs());
                break;

            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                PerformUndo();
                break;

            case Key.Y when Keyboard.Modifiers == ModifierKeys.Control:
                PerformRedo();
                break;

            // Tool shortcuts
            case Key.P: SelectToolByTag("Pen"); break;
            case Key.L: SelectToolByTag("Line"); break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Arrow"); break;
            case Key.R: SelectToolByTag("Rectangle"); break;
            case Key.E: SelectToolByTag("Ellipse"); break;
            case Key.T: SelectToolByTag("Text"); break;
            case Key.M: SelectToolByTag("Marker"); break;
            case Key.B: SelectToolByTag("Blur"); break;

            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
                ThicknessUp_Click(this, new RoutedEventArgs());
                break;
            case Key.OemMinus when Keyboard.Modifiers == ModifierKeys.Control:
                ThicknessDown_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Mouse wheel adjusts thickness when a tool is active
        if (_currentTool != null)
        {
            if (e.Delta > 0)
                ThicknessUp_Click(this, new RoutedEventArgs());
            else
                ThicknessDown_Click(this, new RoutedEventArgs());
        }
    }

    private void SelectToolByTag(string tag)
    {
        if (_mode != Mode.Selected && _mode != Mode.Drawing) return;

        foreach (var child in DrawingToolsPanel.Children)
        {
            if (child is Button btn && btn.Tag is string t && t == tag)
            {
                Tool_Click(btn, new RoutedEventArgs());
                return;
            }
        }
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

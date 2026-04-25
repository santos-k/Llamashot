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
    private enum Interaction { None, Selecting, Resizing, Moving, Drawing }
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

    // Drawing
    private IDrawingTool? _currentTool;
    private readonly Stack<DrawingAction> _undoStack = new();
    private readonly Stack<DrawingAction> _redoStack = new();
    private Color _currentColor = Colors.Red;
    private double _currentThickness = 2;

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
    }

    public void StartCapture()
    {
        _virtualBounds = ScreenCapture.GetVirtualScreenBounds();
        _screenshot = ScreenCapture.CaptureFullScreen();
        ScreenshotImage.Source = _screenshot;

        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;

        UpdateDimming(Rect.Empty);

        _interaction = Interaction.None;
        _hasSelection = false;
        _currentTool = null;
        SelectionBorder.Visibility = Visibility.Collapsed;
        DimensionBorder.Visibility = Visibility.Collapsed;
        ToolbarCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Visibility = Visibility.Collapsed;
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;
        ThicknessPopupCanvas.Visibility = Visibility.Collapsed;
        DrawingCanvas.Children.Clear();
        _undoStack.Clear();
        _redoStack.Clear();

        Show();
        Activate();
        Focus();
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

    // ============ UNIFIED MOUSE HANDLING ON MainCanvas ============

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(MainCanvas);
        ColorPaletteCanvas.Visibility = Visibility.Collapsed;

        if (_hasSelection)
        {
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
            _currentTool = null;
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
                    _hasSelection = true;
                    ShowToolbars();
                    ShowResizeHandles();
                }
                break;

            case Interaction.Resizing:
            case Interaction.Moving:
                ShowResizeHandles();
                UpdateToolbarPositions();
                break;
        }
    }

    private void UpdateSelectionVisuals()
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

        _selection = _activeHandle switch
        {
            Handle.TL => new Rect(r.Left + dx, r.Top + dy, r.Width - dx, r.Height - dy),
            Handle.T  => new Rect(r.Left, r.Top + dy, r.Width, r.Height - dy),
            Handle.TR => new Rect(r.Left, r.Top + dy, r.Width + dx, r.Height - dy),
            Handle.R  => new Rect(r.Left, r.Top, r.Width + dx, r.Height),
            Handle.BR => new Rect(r.Left, r.Top, r.Width + dx, r.Height + dy),
            Handle.B  => new Rect(r.Left, r.Top, r.Width, r.Height + dy),
            Handle.BL => new Rect(r.Left + dx, r.Top, r.Width - dx, r.Height + dy),
            Handle.L  => new Rect(r.Left + dx, r.Top, r.Width - dx, r.Height),
            _ => _selection
        };

        // Enforce minimum
        if (_selection.Width < 10)
            _selection = new Rect(_selection.Left, _selection.Top, 10, _selection.Height);
        if (_selection.Height < 10)
            _selection = new Rect(_selection.Left, _selection.Top, _selection.Width, 10);

        RefreshSelectionUI();
    }

    private void ApplyMove(Point pos)
    {
        var dx = pos.X - _dragStart.X;
        var dy = pos.Y - _dragStart.Y;

        var newSel = new Rect(
            _dragOrigSelection.Left + dx, _dragOrigSelection.Top + dy,
            _dragOrigSelection.Width, _dragOrigSelection.Height);

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
        // Adaptive columns: measure in 1-col, if it doesn't fit use 2-col
        ToolWrapPanel.Width = 32;
        BottomToolsPanel.Width = 32;
        DrawingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double availableHeight = ActualHeight - 8; // total screen minus margin
        if (DrawingToolbar.DesiredSize.Height > availableHeight)
        {
            ToolWrapPanel.Width = 64;
            BottomToolsPanel.Width = 64;
        }

        DrawingToolbar.UpdateLayout();
        DrawingToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        ActionToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double dtTop = _selection.Top;

        // Prefer LEFT side of selection, fallback to right
        double dtLeft = _selection.Left - DrawingToolbar.DesiredSize.Width - 8;
        if (dtLeft < 4)
            dtLeft = _selection.Right + 8;
        if (dtLeft + DrawingToolbar.DesiredSize.Width > ActualWidth)
            dtLeft = 4;

        // Clamp to screen bounds
        if (dtTop + DrawingToolbar.DesiredSize.Height > ActualHeight - 4)
            dtTop = ActualHeight - DrawingToolbar.DesiredSize.Height - 4;
        if (dtTop < 4) dtTop = 4;

        Canvas.SetLeft(DrawingToolbar, dtLeft);
        Canvas.SetTop(DrawingToolbar, dtTop);

        double atLeft = _selection.Left;
        double atTop = _selection.Bottom + 8;
        if (atTop + ActionToolbar.DesiredSize.Height > ActualHeight)
            atTop = _selection.Top - ActionToolbar.DesiredSize.Height - 8;

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

        // Reset all tool button highlights
        ClearToolHighlights();
        btn.Background = new SolidColorBrush(Color.FromArgb(80, 33, 150, 243));

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
            "Check" => new StampTool(StampType.Check),
            "CrossMark" => new StampTool(StampType.Cross),
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
        _currentTool = null;
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
        // Highlight the move button
        if (sender is Button btn)
            btn.Background = new SolidColorBrush(Color.FromArgb(80, 33, 150, 243));
        Focus();
    }

    private void Drawing_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasSelection) return;
        var pos = e.GetPosition(DrawingCanvas);
        if (!_selection.Contains(pos)) return;

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

        _interaction = Interaction.Drawing;
        _currentTool.OnMouseDown(pos, DrawingCanvas);
        DrawingCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void Drawing_MouseMove(object sender, MouseEventArgs e)
    {
        if (_interaction != Interaction.Drawing || _currentTool == null) return;
        if (e.LeftButton != MouseButtonState.Pressed) return;

        _currentTool.OnMouseMove(e.GetPosition(DrawingCanvas), DrawingCanvas);
        e.Handled = true;
    }

    private void Drawing_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
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
        Clipboard.SetImage(RenderFinalImage());
        Close();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        var pd = new PrintDialog();
        if (pd.ShowDialog() == true)
        {
            var v = new DrawingVisual();
            using (var dc = v.RenderOpen())
                dc.DrawImage(image, new Rect(0, 0, pd.PrintableAreaWidth, pd.PrintableAreaHeight));
            pd.PrintVisual(v, "Llamashot Screenshot");
        }
        Close();
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        var image = RenderFinalImage();
        new PinWindow(image, _selection).Show();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ============ KEYBOARD ============

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Skip all shortcuts when typing in a TextBox
        if (Keyboard.FocusedElement is TextBox)
        {
            if (e.Key == Key.Escape)
            {
                // ESC defocuses the textbox
                Focus();
                e.Handled = true;
            }
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
            if (_currentTool != null)
                DeselectTool();
            else
                Close();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.S when Keyboard.Modifiers == ModifierKeys.Control:
                if (_hasSelection) Save_Click(this, new RoutedEventArgs());
                e.Handled = true; break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                if (_hasSelection) Copy_Click(this, new RoutedEventArgs());
                e.Handled = true; break;
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.X when Keyboard.Modifiers == ModifierKeys.Control:
                PerformUndo(); e.Handled = true; break;
            case Key.Y when Keyboard.Modifiers == ModifierKeys.Control:
                PerformRedo(); e.Handled = true; break;

            case Key.P when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Pen"); break;
            case Key.L when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Line"); break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Arrow"); break;
            case Key.R when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Rectangle"); break;
            case Key.E when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Ellipse"); break;
            case Key.T when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Text"); break;
            case Key.M when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Marker"); break;
            case Key.B when Keyboard.Modifiers == ModifierKeys.None: SelectToolByTag("Blur"); break;
            case Key.X when Keyboard.Modifiers == ModifierKeys.None: Eraser_Click(this, new RoutedEventArgs()); break;
            case Key.V when Keyboard.Modifiers == ModifierKeys.None:
                if (_hasSelection) ActivateMoveTool();
                e.Handled = true; break;

            case Key.OemPlus when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.Add when Keyboard.Modifiers == ModifierKeys.Control:
                ThicknessUp_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            case Key.OemMinus when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.Subtract when Keyboard.Modifiers == ModifierKeys.Control:
                ThicknessDown_Click(this, new RoutedEventArgs()); e.Handled = true; break;
        }
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
        foreach (var child in DrawingToolsPanel.Children)
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

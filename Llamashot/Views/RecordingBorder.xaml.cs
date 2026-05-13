using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Llamashot.Views;

public partial class RecordingBorder : Window
{
    private const double HandleSize = 8;
    private const double HitMargin = 10;
    private const double BorderPad = 3;
    private bool _resizable;
    private enum HitZone { None, Move, Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }
    private HitZone _activeZone = HitZone.None;
    private Point _dragStart;
    private double _origLeft, _origTop, _origWidth, _origHeight;

    public RecordingBorder(double x, double y, double width, double height)
    {
        InitializeComponent();
        Left = x - BorderPad;
        Top = y - BorderPad;
        Width = width + BorderPad * 2;
        Height = height + BorderPad * 2;
        SizeChanged += (s, e) => { if (_resizable) UpdateHandles(); };
    }

    public void SetDashed(bool dashed)
    {
        DashedBorder.Visibility = dashed ? Visibility.Visible : Visibility.Collapsed;
        HandleCanvas.Visibility = dashed ? Visibility.Visible : Visibility.Collapsed;
        DimensionLabel.Visibility = dashed ? Visibility.Visible : Visibility.Collapsed;
        SolidBorder.Visibility = dashed ? Visibility.Collapsed : Visibility.Visible;
        _resizable = dashed;
        IsHitTestVisible = dashed;
        Cursor = dashed ? Cursors.SizeAll : Cursors.Arrow;
        if (dashed) UpdateHandles();
    }

    public Rect GetRegionDip()
    {
        return new Rect(Left + BorderPad, Top + BorderPad,
                        Math.Max(10, Width - BorderPad * 2), Math.Max(10, Height - BorderPad * 2));
    }

    private void UpdateHandles()
    {
        HandleCanvas.Children.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1) return;

        const double arm = 22;   // length of corner bracket arm
        const double thick = 4;  // stroke thickness
        const double bar = 18;   // length of midpoint bar
        var cornerBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)); // blue
        var midBrush = Brushes.White;

        // Corner brackets (L-shaped) - blue
        // Top-left
        AddLine(0, 0, arm, 0, thick, cornerBrush);
        AddLine(0, 0, 0, arm, thick, cornerBrush);
        // Top-right
        AddLine(w, 0, w - arm, 0, thick, cornerBrush);
        AddLine(w, 0, w, arm, thick, cornerBrush);
        // Bottom-left
        AddLine(0, h, arm, h, thick, cornerBrush);
        AddLine(0, h, 0, h - arm, thick, cornerBrush);
        // Bottom-right
        AddLine(w, h, w - arm, h, thick, cornerBrush);
        AddLine(w, h, w, h - arm, thick, cornerBrush);

        // Midpoint bars - white
        // Top
        AddLine(w / 2 - bar / 2, 0, w / 2 + bar / 2, 0, thick, midBrush);
        // Bottom
        AddLine(w / 2 - bar / 2, h, w / 2 + bar / 2, h, thick, midBrush);
        // Left
        AddLine(0, h / 2 - bar / 2, 0, h / 2 + bar / 2, thick, midBrush);
        // Right
        AddLine(w, h / 2 - bar / 2, w, h / 2 + bar / 2, thick, midBrush);

        // Update dimension label
        var region = GetRegionDip();
        DimensionText.Text = $"{(int)region.Width} x {(int)region.Height}";
    }

    private void AddLine(double x1, double y1, double x2, double y2, double thickness, Brush stroke)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
            Stroke = stroke, StrokeThickness = thickness,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            IsHitTestVisible = false
        };
        HandleCanvas.Children.Add(line);
    }

    private HitZone GetHitZone(Point pos)
    {
        bool top = pos.Y < HitMargin;
        bool bottom = pos.Y > ActualHeight - HitMargin;
        bool left = pos.X < HitMargin;
        bool right = pos.X > ActualWidth - HitMargin;

        if (top && left) return HitZone.TopLeft;
        if (top && right) return HitZone.TopRight;
        if (bottom && left) return HitZone.BottomLeft;
        if (bottom && right) return HitZone.BottomRight;
        if (top) return HitZone.Top;
        if (bottom) return HitZone.Bottom;
        if (left) return HitZone.Left;
        if (right) return HitZone.Right;
        return HitZone.Move;
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizable) return;

        if (_activeZone != HitZone.None && e.LeftButton == MouseButtonState.Pressed)
        {
            var screenPos = PointToScreen(e.GetPosition(this));
            double dx = screenPos.X - _dragStart.X;
            double dy = screenPos.Y - _dragStart.Y;
            const double minSize = 60;

            double nL = _origLeft, nT = _origTop, nW = _origWidth, nH = _origHeight;

            switch (_activeZone)
            {
                case HitZone.Move:
                    nL = _origLeft + dx; nT = _origTop + dy; break;
                case HitZone.Right:
                    nW = Math.Max(minSize, _origWidth + dx); break;
                case HitZone.Bottom:
                    nH = Math.Max(minSize, _origHeight + dy); break;
                case HitZone.Left:
                    nL = _origLeft + dx; nW = Math.Max(minSize, _origWidth - dx); break;
                case HitZone.Top:
                    nT = _origTop + dy; nH = Math.Max(minSize, _origHeight - dy); break;
                case HitZone.TopLeft:
                    nL = _origLeft + dx; nT = _origTop + dy;
                    nW = Math.Max(minSize, _origWidth - dx); nH = Math.Max(minSize, _origHeight - dy); break;
                case HitZone.TopRight:
                    nT = _origTop + dy;
                    nW = Math.Max(minSize, _origWidth + dx); nH = Math.Max(minSize, _origHeight - dy); break;
                case HitZone.BottomLeft:
                    nL = _origLeft + dx;
                    nW = Math.Max(minSize, _origWidth - dx); nH = Math.Max(minSize, _origHeight + dy); break;
                case HitZone.BottomRight:
                    nW = Math.Max(minSize, _origWidth + dx); nH = Math.Max(minSize, _origHeight + dy); break;
            }

            Left = nL; Top = nT; Width = nW; Height = nH;
            return;
        }

        var zone = GetHitZone(e.GetPosition(this));
        Cursor = zone switch
        {
            HitZone.TopLeft or HitZone.BottomRight => Cursors.SizeNWSE,
            HitZone.TopRight or HitZone.BottomLeft => Cursors.SizeNESW,
            HitZone.Top or HitZone.Bottom => Cursors.SizeNS,
            HitZone.Left or HitZone.Right => Cursors.SizeWE,
            HitZone.Move => Cursors.SizeAll,
            _ => Cursors.Arrow
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_resizable) return;
        var zone = GetHitZone(e.GetPosition(this));

        if (zone == HitZone.Move)
        {
            // Use built-in DragMove for reliable window dragging
            DragMove();
            UpdateHandles();
            return;
        }

        _activeZone = zone;
        _dragStart = PointToScreen(e.GetPosition(this));
        _origLeft = Left; _origTop = Top; _origWidth = Width; _origHeight = Height;
        CaptureMouse();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeZone != HitZone.None)
        {
            _activeZone = HitZone.None;
            ReleaseMouseCapture();
        }
    }
}

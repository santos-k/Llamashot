using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class LineTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Line;
    public override Cursor Cursor => CursorHelper.Get("Line");
    private Line? _line;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Line,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position, position }
        };

        _line = new Line
        {
            X1 = position.X, Y1 = position.Y,
            X2 = position.X, Y2 = position.Y,
            Stroke = new SolidColorBrush(StrokeColor),
            StrokeThickness = Thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        canvas.Children.Add(_line);
        CurrentAction.RenderedElement = _line;
    }

    public override void OnMouseMove(Point position, Canvas canvas)
    {
        if (!IsDrawing || _line == null) return;

        var end = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? SnapToAngle(StartPoint, position)
            : position;

        _line.X2 = end.X;
        _line.Y2 = end.Y;
        if (CurrentAction != null && CurrentAction.Points.Count >= 2)
            CurrentAction.Points[1] = end;
    }

    /// <summary>Snaps endpoint to nearest 0/45/90/135/180 degree angle from start.</summary>
    private static Point SnapToAngle(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var angle = Math.Atan2(dy, dx);
        var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        var length = Math.Sqrt(dx * dx + dy * dy);
        return new Point(start.X + Math.Cos(snapped) * length, start.Y + Math.Sin(snapped) * length);
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _line = null;
    }
}

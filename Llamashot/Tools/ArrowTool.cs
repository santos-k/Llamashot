using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Models;

namespace Llamashot.Tools;

public class ArrowTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Arrow;
    private Path? _arrowPath;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Arrow,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position, position }
        };

        _arrowPath = new Path
        {
            Stroke = new SolidColorBrush(StrokeColor),
            Fill = new SolidColorBrush(StrokeColor),
            StrokeThickness = Thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        canvas.Children.Add(_arrowPath);
        CurrentAction.RenderedElement = _arrowPath;
    }

    public override void OnMouseMove(Point position, Canvas canvas)
    {
        if (!IsDrawing || _arrowPath == null) return;
        _arrowPath.Data = BuildArrowGeometry(StartPoint, position);
        if (CurrentAction != null && CurrentAction.Points.Count >= 2)
            CurrentAction.Points[1] = position;
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _arrowPath = null;
    }

    private Geometry BuildArrowGeometry(Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1) return Geometry.Empty;

        var headLength = Math.Min(Thickness * 5, length * 0.4);
        var headWidth = headLength * 0.6;

        // Normalize direction
        var nx = dx / length;
        var ny = dy / length;

        // Perpendicular
        var px = -ny;
        var py = nx;

        // Arrow head base point
        var baseX = end.X - nx * headLength;
        var baseY = end.Y - ny * headLength;

        var group = new GeometryGroup();

        // Line (shaft)
        group.Children.Add(new LineGeometry(start, new Point(baseX, baseY)));

        // Arrow head (filled triangle)
        var headFig = new PathFigure(end, new[]
        {
            new LineSegment(new Point(baseX + px * headWidth, baseY + py * headWidth), true),
            new LineSegment(new Point(baseX - px * headWidth, baseY - py * headWidth), true),
        }, true);

        var headGeom = new PathGeometry(new[] { headFig });
        group.Children.Add(headGeom);

        return group;
    }
}

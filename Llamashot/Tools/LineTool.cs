using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Models;

namespace Llamashot.Tools;

public class LineTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Line;
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
        _line.X2 = position.X;
        _line.Y2 = position.Y;
        if (CurrentAction != null && CurrentAction.Points.Count >= 2)
            CurrentAction.Points[1] = position;
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _line = null;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class RectangleTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Rectangle;
    public override Cursor Cursor => CursorHelper.Get("Rectangle");
    private Rectangle? _rectangle;
    private bool _filled;

    public RectangleTool(bool filled = false)
    {
        _filled = filled;
    }

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;

        CurrentAction = new DrawingAction
        {
            ToolType = _filled ? DrawingToolType.FilledRectangle : DrawingToolType.Rectangle,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position, position }
        };

        _rectangle = new Rectangle
        {
            Stroke = new SolidColorBrush(StrokeColor),
            StrokeThickness = Thickness,
            Fill = _filled ? new SolidColorBrush(Color.FromArgb(80, StrokeColor.R, StrokeColor.G, StrokeColor.B)) : null
        };

        Canvas.SetLeft(_rectangle, position.X);
        Canvas.SetTop(_rectangle, position.Y);
        canvas.Children.Add(_rectangle);
        CurrentAction.RenderedElement = _rectangle;
    }

    public override void OnMouseMove(Point position, Canvas canvas)
    {
        if (!IsDrawing || _rectangle == null) return;

        var x = Math.Min(StartPoint.X, position.X);
        var y = Math.Min(StartPoint.Y, position.Y);
        var w = Math.Abs(position.X - StartPoint.X);
        var h = Math.Abs(position.Y - StartPoint.Y);

        Canvas.SetLeft(_rectangle, x);
        Canvas.SetTop(_rectangle, y);
        _rectangle.Width = w;
        _rectangle.Height = h;

        if (CurrentAction != null)
            CurrentAction.Bounds = new Rect(x, y, w, h);
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _rectangle = null;
    }
}

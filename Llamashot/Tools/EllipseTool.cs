using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class EllipseTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Ellipse;
    public override Cursor Cursor => CursorHelper.Get("Ellipse");
    private Ellipse? _ellipse;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Ellipse,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position, position }
        };

        _ellipse = new Ellipse
        {
            Stroke = new SolidColorBrush(StrokeColor),
            StrokeThickness = Thickness
        };

        Canvas.SetLeft(_ellipse, position.X);
        Canvas.SetTop(_ellipse, position.Y);
        canvas.Children.Add(_ellipse);
        CurrentAction.RenderedElement = _ellipse;
    }

    public override void OnMouseMove(Point position, Canvas canvas)
    {
        if (!IsDrawing || _ellipse == null) return;

        var x = Math.Min(StartPoint.X, position.X);
        var y = Math.Min(StartPoint.Y, position.Y);
        var w = Math.Abs(position.X - StartPoint.X);
        var h = Math.Abs(position.Y - StartPoint.Y);

        Canvas.SetLeft(_ellipse, x);
        Canvas.SetTop(_ellipse, y);
        _ellipse.Width = w;
        _ellipse.Height = h;

        if (CurrentAction != null)
            CurrentAction.Bounds = new Rect(x, y, w, h);
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _ellipse = null;
    }
}

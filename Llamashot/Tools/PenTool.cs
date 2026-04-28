using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class PenTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Pen;
    public override Cursor Cursor => CursorHelper.Get("Pen");
    private Polyline? _polyline;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Pen,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position }
        };

        _polyline = new Polyline
        {
            Stroke = new SolidColorBrush(StrokeColor),
            StrokeThickness = Thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Points = new PointCollection { position }
        };

        canvas.Children.Add(_polyline);
        CurrentAction.RenderedElement = _polyline;
    }

    public override void OnMouseMove(Point position, Canvas canvas)
    {
        if (!IsDrawing || _polyline == null) return;
        _polyline.Points.Add(position);
        CurrentAction?.Points.Add(position);
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _polyline = null;
    }
}

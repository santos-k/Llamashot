using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Models;

namespace Llamashot.Tools;

public enum StampType { Check, Cross }

public class StampTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Pen; // reuse
    public StampType Stamp { get; }
    public double StampSize { get; set; } = 28;

    public StampTool(StampType stamp)
    {
        Stamp = stamp;
    }

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        var path = new Path
        {
            StrokeThickness = 3,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };

        double s = StampSize;
        double x = position.X - s / 2;
        double y = position.Y - s / 2;

        if (Stamp == StampType.Check)
        {
            path.Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
            path.Data = Geometry.Parse($"M{x + s * 0.15},{y + s * 0.55} L{x + s * 0.4},{y + s * 0.8} L{x + s * 0.85},{y + s * 0.2}");
        }
        else
        {
            path.Stroke = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Red
            var group = new GeometryGroup();
            group.Children.Add(Geometry.Parse($"M{x + s * 0.2},{y + s * 0.2} L{x + s * 0.8},{y + s * 0.8}"));
            group.Children.Add(Geometry.Parse($"M{x + s * 0.8},{y + s * 0.2} L{x + s * 0.2},{y + s * 0.8}"));
            path.Data = group;
        }

        canvas.Children.Add(path);

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Pen,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position },
            RenderedElement = path
        };
    }

    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }
}

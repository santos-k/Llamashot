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
    public override DrawingToolType ToolType => DrawingToolType.Pen;
    public StampType Stamp { get; }
    public double StampSize { get; set; } = 36;

    public StampTool(StampType stamp)
    {
        Stamp = stamp;
    }

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        double s = StampSize;
        double x = position.X - s / 2;
        double y = position.Y - s / 2;

        var group = new System.Windows.Controls.Canvas();
        Canvas.SetLeft(group, x);
        Canvas.SetTop(group, y);
        group.Width = s;
        group.Height = s;

        // Circle background
        var circle = new Ellipse
        {
            Width = s, Height = s,
            StrokeThickness = 2.5,
        };

        var mark = new Path
        {
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };

        if (Stamp == StampType.Check)
        {
            var green = Color.FromRgb(0x4C, 0xAF, 0x50);
            circle.Stroke = new SolidColorBrush(green);
            circle.Fill = new SolidColorBrush(Color.FromArgb(40, 0x4C, 0xAF, 0x50));
            mark.Stroke = new SolidColorBrush(green);
            mark.Data = Geometry.Parse($"M{s * 0.22},{s * 0.52} L{s * 0.42},{s * 0.72} L{s * 0.78},{s * 0.28}");
        }
        else
        {
            var red = Color.FromRgb(0xF4, 0x43, 0x36);
            circle.Stroke = new SolidColorBrush(red);
            circle.Fill = new SolidColorBrush(Color.FromArgb(40, 0xF4, 0x43, 0x36));
            mark.Stroke = new SolidColorBrush(red);
            var g = new GeometryGroup();
            g.Children.Add(Geometry.Parse($"M{s * 0.28},{s * 0.28} L{s * 0.72},{s * 0.72}"));
            g.Children.Add(Geometry.Parse($"M{s * 0.72},{s * 0.28} L{s * 0.28},{s * 0.72}"));
            mark.Data = g;
        }

        group.Children.Add(circle);
        group.Children.Add(mark);
        canvas.Children.Add(group);

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Pen,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position },
            RenderedElement = group
        };
    }

    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class MarkerTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Marker;
    public override Cursor Cursor => CursorHelper.Get("Marker");
    private Polyline? _polyline;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Marker,
            StrokeColor = StrokeColor,
            Thickness = Thickness * 6, // Marker is thicker
            Points = new List<Point> { position }
        };

        _polyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromArgb(80, StrokeColor.R, StrokeColor.G, StrokeColor.B)),
            StrokeThickness = Thickness * 6,
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

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Constrain to straight line from start: snap to nearest axis
            var dx = Math.Abs(position.X - StartPoint.X);
            var dy = Math.Abs(position.Y - StartPoint.Y);
            var snapped = dx >= dy
                ? new Point(position.X, StartPoint.Y)  // horizontal
                : new Point(StartPoint.X, position.Y);  // vertical

            // Replace all points with just start → current for a clean line
            _polyline.Points.Clear();
            _polyline.Points.Add(StartPoint);
            _polyline.Points.Add(snapped);
            CurrentAction?.Points.Clear();
            CurrentAction?.Points.Add(StartPoint);
            CurrentAction?.Points.Add(snapped);
        }
        else
        {
            _polyline.Points.Add(position);
            CurrentAction?.Points.Add(position);
        }
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        IsDrawing = false;
        _polyline = null;
    }
}

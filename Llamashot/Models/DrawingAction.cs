using System.Windows;
using System.Windows.Media;

namespace Llamashot.Models;

public enum DrawingToolType
{
    None,
    Pen,
    Line,
    Arrow,
    Rectangle,
    FilledRectangle,
    Ellipse,
    Text,
    Marker,
    Blur,
    Eraser
}

public class DrawingAction
{
    public DrawingToolType ToolType { get; set; }
    public Color StrokeColor { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 2;
    public List<Point> Points { get; set; } = new();
    public Rect Bounds { get; set; }
    public string? Text { get; set; }
    public double FontSize { get; set; } = 16;
    public UIElement? RenderedElement { get; set; }
    public UIElement? ErasedElement { get; set; }  // For eraser: the element that was removed
}

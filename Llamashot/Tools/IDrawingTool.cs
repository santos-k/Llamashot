using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Llamashot.Tools;

public interface IDrawingTool
{
    Models.DrawingToolType ToolType { get; }
    Cursor Cursor { get; }
    void OnMouseDown(Point position, Canvas canvas);
    void OnMouseMove(Point position, Canvas canvas);
    void OnMouseUp(Point position, Canvas canvas);
    Color StrokeColor { get; set; }
    double Thickness { get; set; }
    Models.DrawingAction? CurrentAction { get; }
}

public abstract class BaseDrawingTool : IDrawingTool
{
    public abstract Models.DrawingToolType ToolType { get; }
    public virtual Cursor Cursor => Cursors.Cross;
    public Color StrokeColor { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 2;
    public Models.DrawingAction? CurrentAction { get; protected set; }
    protected bool IsDrawing { get; set; }
    protected Point StartPoint { get; set; }

    public abstract void OnMouseDown(Point position, Canvas canvas);
    public abstract void OnMouseMove(Point position, Canvas canvas);
    public abstract void OnMouseUp(Point position, Canvas canvas);
}

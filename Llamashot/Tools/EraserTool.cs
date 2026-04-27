using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class EraserTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Eraser;
    public override Cursor Cursor => CursorHelper.Get("Eraser");

    // Eraser logic is handled in OverlayWindow; these are no-ops
    public override void OnMouseDown(Point position, Canvas canvas) { }
    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }
}

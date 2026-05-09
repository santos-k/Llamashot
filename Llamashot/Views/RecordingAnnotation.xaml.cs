using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Llamashot.Core;
using Llamashot.Tools;

namespace Llamashot.Views;

public partial class RecordingAnnotation : Window
{
    private IDrawingTool? _currentTool;
    private bool _drawing;
    private IntPtr _hwnd;

    public event Action? EscapePressed;
    public event Action? StrokeCompleted;

    public RecordingAnnotation(double x, double y, double width, double height)
    {
        InitializeComponent();
        Left = x;
        Top = y;
        Width = width;
        Height = height;

        Loaded += (s, e) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Start as click-through so it doesn't block other apps
            SetClickThrough(true);
        };
    }

    public void SetTool(IDrawingTool? tool)
    {
        _currentTool = tool;
        Cursor = tool?.Cursor ?? Cursors.Arrow;
        // Win32 level: toggle click-through
        SetClickThrough(tool == null);
    }

    public void ClearAll()
    {
        DrawingCanvas.Children.Clear();
    }

    private void SetClickThrough(bool passThrough)
    {
        if (_hwnd == IntPtr.Zero) return;
        int style = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        if (passThrough)
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style | NativeMethods.WS_EX_TRANSPARENT);
        else
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, style & ~NativeMethods.WS_EX_TRANSPARENT);
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == null) return;
        _drawing = true;
        _currentTool.OnMouseDown(e.GetPosition(DrawingCanvas), DrawingCanvas);
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _currentTool == null) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishDrawing(e.GetPosition(DrawingCanvas));
            return;
        }

        _currentTool.OnMouseMove(e.GetPosition(DrawingCanvas), DrawingCanvas);
        e.Handled = true;
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing || _currentTool == null) return;
        FinishDrawing(e.GetPosition(DrawingCanvas));
        e.Handled = true;
    }

    private void FinishDrawing(Point position)
    {
        _drawing = false;
        _currentTool?.OnMouseUp(position, DrawingCanvas);
        StrokeCompleted?.Invoke();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_drawing) FinishDrawing(new Point(0, 0));
            EscapePressed?.Invoke();
            e.Handled = true;
        }
    }
}

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
        // Finalize any active text before switching
        if (_currentTool is TextTool prevText)
            prevText.FinalizeActiveTextBox();

        _currentTool = tool;
        Cursor = tool?.Cursor ?? Cursors.Arrow;
        // Win32 level: toggle click-through
        SetClickThrough(tool == null);

        // TextTool: fire StrokeCompleted when text entry is finalized
        if (tool is TextTool textTool)
        {
            textTool.Finalized = () => StrokeCompleted?.Invoke();
        }
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

        // TextTool: stay interactive for typing, StrokeCompleted fires on Finalized
        if (_currentTool is TextTool)
            return;

        StrokeCompleted?.Invoke();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // If text tool is active, just finalize the text instead of closing
            if (_currentTool is TextTool textTool)
            {
                textTool.FinalizeActiveTextBox();
                e.Handled = true;
                return;
            }
            if (_drawing) FinishDrawing(new Point(0, 0));
            EscapePressed?.Invoke();
            e.Handled = true;
        }
    }
}

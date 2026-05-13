using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Llamashot.Core;
using Llamashot.Tools;

namespace Llamashot.Views;

public partial class RecordingAnnotation : Window
{
    private IDrawingTool? _currentTool;
    private bool _drawing;
    private bool _eraserMode;
    private IntPtr _hwnd;

    /// <summary>True when a TextBox in the annotation overlay has keyboard focus.</summary>
    internal static bool IsTextInputActive { get; set; }

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

        // Track TextBox focus to suppress global shortcuts during text input
        DrawingCanvas.AddHandler(UIElement.GotKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((s, e) =>
        {
            if (e.NewFocus is System.Windows.Controls.TextBox) IsTextInputActive = true;
        }), true);
        DrawingCanvas.AddHandler(UIElement.LostKeyboardFocusEvent, new System.Windows.Input.KeyboardFocusChangedEventHandler((s, e) =>
        {
            if (e.OldFocus is System.Windows.Controls.TextBox) IsTextInputActive = false;
        }), true);
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

    public void FinalizeText()
    {
        if (_currentTool is TextTool tt)
            tt.FinalizeActiveTextBox();
        IsTextInputActive = false;
    }

    public void Undo()
    {
        if (DrawingCanvas.Children.Count > 0)
            DrawingCanvas.Children.RemoveAt(DrawingCanvas.Children.Count - 1);
    }

    public void ClearAll()
    {
        DrawingCanvas.Children.Clear();
    }

    public void SetEraserMode(bool enabled)
    {
        _eraserMode = enabled;
        if (enabled)
        {
            _currentTool = null;
            Cursor = Cursors.Hand;
            SetClickThrough(false);
        }
        else
        {
            SetClickThrough(_currentTool == null);
        }
    }

    private void EraseElementAt(Point pos)
    {
        UIElement? hit = null;
        var hitArea = new EllipseGeometry(pos, 6, 6);
        var hitParams = new GeometryHitTestParameters(hitArea);

        VisualTreeHelper.HitTest(DrawingCanvas, null,
            result =>
            {
                var visual = result.VisualHit;
                while (visual != null && visual != DrawingCanvas)
                {
                    var parent = VisualTreeHelper.GetParent(visual);
                    if (parent == DrawingCanvas) { hit = visual as UIElement; return HitTestResultBehavior.Stop; }
                    visual = parent as DependencyObject;
                }
                return HitTestResultBehavior.Continue;
            },
            hitParams);

        if (hit != null)
        {
            DrawingCanvas.Children.Remove(hit);
            StrokeCompleted?.Invoke();
        }
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
        if (_eraserMode)
        {
            EraseElementAt(e.GetPosition(DrawingCanvas));
            e.Handled = true;
            return;
        }
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
        // Enter or Escape while typing: finalize text and dismiss
        if ((e.Key == Key.Enter || e.Key == Key.Escape) && _currentTool is TextTool textTool && IsTextInputActive)
        {
            textTool.FinalizeActiveTextBox();
            IsTextInputActive = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_drawing) FinishDrawing(new Point(0, 0));
            EscapePressed?.Invoke();
            e.Handled = true;
        }
    }
}

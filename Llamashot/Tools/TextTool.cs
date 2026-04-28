using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Llamashot.Core;
using Llamashot.Models;

namespace Llamashot.Tools;

public class TextTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Text;
    public override Cursor Cursor => CursorHelper.Get("Text");
    public double FontSize { get; set; } = 16;
    private TextBox? _activeTextBox;
    private Canvas? _canvas;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        // If there's already an active textbox, finalize it first
        FinalizeActiveTextBox();

        _canvas = canvas;

        // Font size follows thickness: thickness 1=12px, 3=16px, 5=20px, 10=30px
        double fontSize = 10 + Thickness * 2;

        _activeTextBox = new TextBox
        {
            Foreground = new SolidColorBrush(StrokeColor),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(128, StrokeColor.R, StrokeColor.G, StrokeColor.B)),
            BorderThickness = new Thickness(1),
            FontSize = fontSize,
            FontFamily = new FontFamily("Segoe UI"),
            MinWidth = 80,
            MinHeight = (int)(fontSize * 1.6),
            Padding = new Thickness(4, 2, 4, 2),
            AcceptsReturn = true,
            CaretBrush = new SolidColorBrush(StrokeColor)
        };

        Canvas.SetLeft(_activeTextBox, position.X);
        Canvas.SetTop(_activeTextBox, position.Y);
        canvas.Children.Add(_activeTextBox);

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Text,
            StrokeColor = StrokeColor,
            FontSize = fontSize,
            Points = new List<Point> { position },
            RenderedElement = _activeTextBox
        };

        var tb = _activeTextBox;
        tb.Loaded += (s, e) =>
        {
            tb.Focus();
            Keyboard.Focus(tb);
        };

        // Ctrl+Enter or Escape finalizes
        tb.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                FinalizeActiveTextBox();
                e.Handled = true;
            }
        };

        tb.LostFocus += (s, e) => FinalizeTextBox(tb, canvas);
    }

    public void FinalizeActiveTextBox()
    {
        if (_activeTextBox != null && _canvas != null)
        {
            FinalizeTextBox(_activeTextBox, _canvas);
            _activeTextBox = null;
        }
    }

    private void FinalizeTextBox(TextBox textBox, Canvas canvas)
    {
        if (!canvas.Children.Contains(textBox)) return;

        if (string.IsNullOrWhiteSpace(textBox.Text))
        {
            canvas.Children.Remove(textBox);
            if (CurrentAction?.RenderedElement == textBox)
                CurrentAction = null;
            return;
        }

        var textBlock = new TextBlock
        {
            Text = textBox.Text,
            Foreground = textBox.Foreground,
            FontSize = textBox.FontSize,
            FontFamily = textBox.FontFamily,
            TextWrapping = TextWrapping.Wrap
        };

        Canvas.SetLeft(textBlock, Canvas.GetLeft(textBox));
        Canvas.SetTop(textBlock, Canvas.GetTop(textBox));

        var idx = canvas.Children.IndexOf(textBox);
        canvas.Children.Remove(textBox);
        if (idx >= 0 && idx <= canvas.Children.Count)
            canvas.Children.Insert(idx, textBlock);
        else
            canvas.Children.Add(textBlock);

        if (CurrentAction?.RenderedElement == textBox)
        {
            CurrentAction.Text = textBox.Text;
            CurrentAction.RenderedElement = textBlock;
        }

        if (_activeTextBox == textBox)
            _activeTextBox = null;
    }

    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }
}

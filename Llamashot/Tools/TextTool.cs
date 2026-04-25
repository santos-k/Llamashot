using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Llamashot.Models;

namespace Llamashot.Tools;

public class TextTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Text;
    public override Cursor Cursor => Cursors.IBeam;
    public double FontSize { get; set; } = 16;

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        var textBox = new TextBox
        {
            Foreground = new SolidColorBrush(StrokeColor),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(128, StrokeColor.R, StrokeColor.G, StrokeColor.B)),
            BorderThickness = new Thickness(1),
            FontSize = FontSize,
            FontFamily = new FontFamily("Segoe UI"),
            MinWidth = 100,
            MinHeight = 28,
            Padding = new Thickness(4, 2, 4, 2),
            AcceptsReturn = true,
            CaretBrush = new SolidColorBrush(StrokeColor)
        };

        Canvas.SetLeft(textBox, position.X);
        Canvas.SetTop(textBox, position.Y);
        canvas.Children.Add(textBox);

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Text,
            StrokeColor = StrokeColor,
            FontSize = FontSize,
            Points = new List<Point> { position },
            RenderedElement = textBox
        };

        // Focus the textbox
        textBox.Loaded += (s, e) =>
        {
            textBox.Focus();
            Keyboard.Focus(textBox);
        };

        // Finalize on lost focus
        textBox.LostFocus += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                canvas.Children.Remove(textBox);
                return;
            }

            // Convert to TextBlock for final rendering
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
            canvas.Children.Insert(idx, textBlock);

            if (CurrentAction != null)
            {
                CurrentAction.Text = textBox.Text;
                CurrentAction.RenderedElement = textBlock;
            }
        };
    }

    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }
}

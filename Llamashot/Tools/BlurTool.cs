using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Llamashot.Models;

namespace Llamashot.Tools;

public class BlurTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Blur;
    private System.Windows.Shapes.Rectangle? _blurRect;
    private Canvas? _parentCanvas;

    public System.Windows.Media.Imaging.BitmapSource? ScreenshotSource { get; set; }

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        IsDrawing = true;
        StartPoint = position;
        _parentCanvas = canvas;

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Blur,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position, position }
        };

        // Create a pixelated/mosaic overlay rectangle
        _blurRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) // Nearly transparent to show we're selecting
        };

        Canvas.SetLeft(_blurRect, position.X);
        Canvas.SetTop(_blurRect, position.Y);
        canvas.Children.Add(_blurRect);
    }

    public override void OnMouseMove(Point position, Canvas canvas)
    {
        if (!IsDrawing || _blurRect == null) return;

        var x = Math.Min(StartPoint.X, position.X);
        var y = Math.Min(StartPoint.Y, position.Y);
        var w = Math.Abs(position.X - StartPoint.X);
        var h = Math.Abs(position.Y - StartPoint.Y);

        Canvas.SetLeft(_blurRect, x);
        Canvas.SetTop(_blurRect, y);
        _blurRect.Width = w;
        _blurRect.Height = h;

        if (CurrentAction != null)
            CurrentAction.Bounds = new Rect(x, y, w, h);
    }

    public override void OnMouseUp(Point position, Canvas canvas)
    {
        if (!IsDrawing || _blurRect == null) return;
        IsDrawing = false;

        var bounds = CurrentAction?.Bounds ?? Rect.Empty;
        if (bounds.Width < 5 || bounds.Height < 5)
        {
            canvas.Children.Remove(_blurRect);
            _blurRect = null;
            return;
        }

        // Replace the selection rectangle with a blurred version
        canvas.Children.Remove(_blurRect);

        // Create a pixelation effect by rendering the region at very low resolution
        if (ScreenshotSource != null)
        {
            var blurredImage = CreatePixelatedRegion(bounds);
            if (blurredImage != null)
            {
                canvas.Children.Add(blurredImage);
                if (CurrentAction != null)
                    CurrentAction.RenderedElement = blurredImage;
            }
        }

        _blurRect = null;
    }

    private UIElement? CreatePixelatedRegion(Rect bounds)
    {
        if (ScreenshotSource == null) return null;

        try
        {
            // Create a rectangle with heavy blur effect
            var rect = new Rectangle
            {
                Width = bounds.Width,
                Height = bounds.Height,
                Fill = CreatePixelatedBrush(bounds),
                Effect = new BlurEffect { Radius = 15, KernelType = KernelType.Gaussian }
            };

            Canvas.SetLeft(rect, bounds.X);
            Canvas.SetTop(rect, bounds.Y);
            return rect;
        }
        catch
        {
            return null;
        }
    }

    private Brush CreatePixelatedBrush(Rect bounds)
    {
        if (ScreenshotSource == null)
            return new SolidColorBrush(Colors.Gray);

        try
        {
            var cropped = new System.Windows.Media.Imaging.CroppedBitmap(
                ScreenshotSource,
                new Int32Rect((int)bounds.X, (int)bounds.Y,
                    Math.Min((int)bounds.Width, ScreenshotSource.PixelWidth - (int)bounds.X),
                    Math.Min((int)bounds.Height, ScreenshotSource.PixelHeight - (int)bounds.Y)));

            return new ImageBrush(cropped) { Stretch = Stretch.Fill };
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }
}

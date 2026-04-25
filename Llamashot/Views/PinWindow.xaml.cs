using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Llamashot.Views;

public partial class PinWindow : Window
{
    public PinWindow(BitmapSource image, Rect originalBounds)
    {
        InitializeComponent();

        PinnedImage.Source = image;

        // Size to image, but cap at reasonable size
        var maxW = SystemParameters.PrimaryScreenWidth * 0.6;
        var maxH = SystemParameters.PrimaryScreenHeight * 0.6;
        var w = Math.Min(image.PixelWidth, maxW);
        var h = Math.Min(image.PixelHeight, maxH);

        Width = w + 4; // border
        Height = h + 4;

        // Position at original location if possible
        Left = Math.Min(originalBounds.X, SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Min(originalBounds.Y, SystemParameters.VirtualScreenHeight - Height);

        MouseEnter += (s, e) => CloseBtn.Opacity = 1;
        MouseLeave += (s, e) => CloseBtn.Opacity = 0;

        // Mouse wheel to adjust opacity
        MouseWheel += (s, e) =>
        {
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.1 : -0.1), 0.1, 1.0);
            OpacityLabel.Text = $"{(int)(Opacity * 100)}%";
            OpacityLabel.Visibility = Visibility.Visible;
        };
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to copy to clipboard
            if (PinnedImage.Source is BitmapSource bmp)
                Clipboard.SetImage(bmp);
            return;
        }
        DragMove();
    }

    private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

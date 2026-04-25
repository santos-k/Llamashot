using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

public static class ScreenCapture
{
    public static BitmapSource CaptureFullScreen()
    {
        int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);

        return CaptureRegion(x, y, width, height);
    }

    public static BitmapSource CaptureRegion(int x, int y, int width, int height)
    {
        IntPtr hdcScreen = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr hdcMem = NativeMethods.CreateCompatibleDC(hdcScreen);
        IntPtr hBitmap = NativeMethods.CreateCompatibleBitmap(hdcScreen, width, height);
        IntPtr hOld = NativeMethods.SelectObject(hdcMem, hBitmap);

        NativeMethods.BitBlt(hdcMem, 0, 0, width, height, hdcScreen, x, y,
            NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

        NativeMethods.SelectObject(hdcMem, hOld);

        var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
            hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmapSource.Freeze();

        NativeMethods.DeleteObject(hBitmap);
        NativeMethods.DeleteDC(hdcMem);
        NativeMethods.ReleaseDC(IntPtr.Zero, hdcScreen);

        return bitmapSource;
    }

    public static System.Drawing.Bitmap BitmapSourceToDrawingBitmap(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new System.IO.MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new System.Drawing.Bitmap(stream);
    }

    public static BitmapSource CropBitmap(BitmapSource source, Int32Rect rect)
    {
        // Clamp to bounds
        int x = Math.Max(0, rect.X);
        int y = Math.Max(0, rect.Y);
        int w = Math.Min(rect.Width, source.PixelWidth - x);
        int h = Math.Min(rect.Height, source.PixelHeight - y);

        if (w <= 0 || h <= 0)
            return source;

        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
        cropped.Freeze();
        return cropped;
    }

    public static Rect GetVirtualScreenBounds()
    {
        int x = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
        int y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
        int w = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int h = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        return new Rect(x, y, w, h);
    }
}

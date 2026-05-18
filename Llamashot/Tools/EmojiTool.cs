using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using Llamashot.Models;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice;
using Vortice.Mathematics;
using DCommon = Vortice.DCommon;

namespace Llamashot.Tools;

public class EmojiTool : BaseDrawingTool
{
    public override DrawingToolType ToolType => DrawingToolType.Emoji;
    public override Cursor Cursor => CursorHelper.Get("Emoji");
    public string Emoji { get; set; }

    public EmojiTool(string emoji = "👍")
    {
        Emoji = emoji;
    }

    public override void OnMouseDown(Point position, Canvas canvas)
    {
        double fontSize = 12 + Thickness * 4;
        var image = RenderEmoji(Emoji, (float)fontSize);
        if (image == null) return;

        double offset = image.Width / 2;
        Canvas.SetLeft(image, position.X - offset);
        Canvas.SetTop(image, position.Y - offset);
        canvas.Children.Add(image);

        CurrentAction = new DrawingAction
        {
            ToolType = DrawingToolType.Emoji,
            StrokeColor = StrokeColor,
            Thickness = Thickness,
            Points = new List<Point> { position },
            Text = Emoji,
            FontSize = fontSize,
            RenderedElement = image
        };
    }

    public override void OnMouseMove(Point position, Canvas canvas) { }
    public override void OnMouseUp(Point position, Canvas canvas) { }

    // ============ Direct2D Color Emoji Renderer ============
    // Direct2D + DirectWrite with DrawTextOptions.EnableColorFont
    // renders COLR/CPAL color emoji properly.

    private static readonly Dictionary<(string, int), BitmapSource> _cache = new();
    private static ID2D1Factory? _d2dFactory;
    private static IDWriteFactory? _dwFactory;

    public static Image? RenderEmoji(string emoji, float fontSize)
    {
        int sizeKey = (int)fontSize;
        if (_cache.TryGetValue((emoji, sizeKey), out var cached))
            return new Image { Source = cached, Width = cached.PixelWidth, Height = cached.PixelHeight };

        try
        {
            var source = RenderViaD2D(emoji, fontSize);
            if (source == null) return null;
            _cache[(emoji, sizeKey)] = source;
            return new Image { Source = source, Width = source.PixelWidth, Height = source.PixelHeight };
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? RenderViaD2D(string emoji, float fontSize)
    {
        // Lazy-init factories (reused across calls)
        _d2dFactory ??= D2D1.D2D1CreateFactory<ID2D1Factory>();
        _dwFactory ??= DWrite.DWriteCreateFactory<IDWriteFactory>();

        // Create text format + layout for measuring
        using var textFormat = _dwFactory.CreateTextFormat("Segoe UI Emoji", fontSize);
        using var textLayout = _dwFactory.CreateTextLayout(emoji, textFormat, 500, 500);
        var metrics = textLayout.Metrics;

        int w = (int)Math.Ceiling(metrics.WidthIncludingTrailingWhitespace) + 4;
        int h = (int)Math.Ceiling(metrics.Height) + 4;
        if (w <= 4 || h <= 4) return null;

        // Create GDI DC + DIB section for pixel extraction
        IntPtr screenDC = GetDC(IntPtr.Zero);
        IntPtr memDC = CreateCompatibleDC(screenDC);
        ReleaseDC(IntPtr.Zero, screenDC);

        var bmi = new BITMAPINFOHEADER
        {
            biSize = 40, biWidth = w, biHeight = -h,
            biPlanes = 1, biBitCount = 32
        };
        IntPtr hBitmap = CreateDIBSection(memDC, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero) { DeleteDC(memDC); return null; }
        IntPtr oldBmp = SelectObject(memDC, hBitmap);

        // Clear to transparent
        int bufLen = w * h * 4;
        Marshal.Copy(new byte[bufLen], 0, bits, bufLen);

        // Create D2D render target bound to our GDI DC
        var rtProps = new RenderTargetProperties
        {
            PixelFormat = new DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied),
            DpiX = 96, DpiY = 96
        };
        using var dcRt = _d2dFactory.CreateDCRenderTarget(rtProps);
        dcRt.BindDC(memDC, new RawRect(0, 0, w, h));

        // Render with color font support
        dcRt.BeginDraw();
        dcRt.Clear(new Color4(0, 0, 0, 0));
        using var brush = dcRt.CreateSolidColorBrush(new Color4(0, 0, 0, 1));
        dcRt.DrawTextLayout(new Vector2(0, 0), textLayout, brush, DrawTextOptions.EnableColorFont);
        dcRt.EndDraw();

        // Read pixels from DIB section
        byte[] pixels = new byte[bufLen];
        Marshal.Copy(bits, pixels, 0, bufLen);

        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, pixels, w * 4);
        source.Freeze();

        // Cleanup GDI resources
        SelectObject(memDC, oldBmp);
        DeleteObject(hBitmap);
        DeleteDC(memDC);

        return source;
    }

    // ============ P/Invoke (GDI for DIB section) ============

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }
}

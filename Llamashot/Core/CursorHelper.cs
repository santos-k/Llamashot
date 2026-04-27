using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

public static class CursorHelper
{
    private static readonly Dictionary<string, Cursor> _cache = new();

    public static Cursor Get(string toolName)
    {
        if (_cache.TryGetValue(toolName, out var cached))
            return cached;

        var cursor = CreateToolCursor(toolName);
        _cache[toolName] = cursor;
        return cursor;
    }

    private static Cursor CreateToolCursor(string toolName)
    {
        try
        {
            return toolName switch
            {
                "Pen" => BuildCursor(DrawPencil, 1, 30),
                "Line" => BuildCursor(DrawLineTool, 16, 16),
                "Arrow" => BuildCursor(DrawArrowTool, 16, 16),
                "Rectangle" => BuildCursor(DrawRectTool, 16, 16),
                "Ellipse" => BuildCursor(DrawEllipseTool, 16, 16),
                "Text" => Cursors.IBeam,
                "Marker" => BuildCursor(DrawMarkerTool, 2, 30),
                "Blur" => BuildCursor(DrawBlurTool, 16, 16),
                "Check" => BuildCursor(DrawCheckStamp, 16, 16),
                "CrossMark" => BuildCursor(DrawCrossStamp, 16, 16),
                "Eraser" => BuildCursor(DrawEraserTool, 16, 28),
                _ => Cursors.Cross
            };
        }
        catch
        {
            return Cursors.Cross;
        }
    }

    private static Cursor BuildCursor(Action<DrawingContext> draw, int hotX, int hotY)
    {
        const int size = 32;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            draw(dc);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);

        var pixels = new byte[size * size * 4];
        rtb.CopyPixels(pixels, size * 4, 0);

        return CreateCursorFromPixels(pixels, size, size, hotX, hotY);
    }

    private static Cursor CreateCursorFromPixels(byte[] pixels, int w, int h, int hotX, int hotY)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICONDIR header
        bw.Write((short)0);     // Reserved
        bw.Write((short)2);     // Type: 2 = cursor
        bw.Write((short)1);     // Image count

        // ICONDIRENTRY
        bw.Write((byte)w);      // Width
        bw.Write((byte)h);      // Height
        bw.Write((byte)0);      // Color count
        bw.Write((byte)0);      // Reserved
        bw.Write((short)hotX);  // Hotspot X
        bw.Write((short)hotY);  // Hotspot Y

        int headerSize = 40;
        int pixelSize = w * h * 4;
        int maskRowBytes = ((w + 31) / 32) * 4;
        int maskSize = maskRowBytes * h;
        int dataSize = headerSize + pixelSize + maskSize;

        bw.Write(dataSize);     // Data size
        bw.Write(22);           // Offset (6 header + 16 entry = 22)

        // BITMAPINFOHEADER
        bw.Write(40);           // biSize
        bw.Write(w);            // biWidth
        bw.Write(h * 2);        // biHeight (doubled for XOR+AND)
        bw.Write((short)1);     // biPlanes
        bw.Write((short)32);    // biBitCount
        bw.Write(0);            // biCompression
        bw.Write(pixelSize + maskSize);
        bw.Write(0);            // biXPelsPerMeter
        bw.Write(0);            // biYPelsPerMeter
        bw.Write(0);            // biClrUsed
        bw.Write(0);            // biClrImportant

        // XOR pixel data (bottom-up BGRA)
        for (int y = h - 1; y >= 0; y--)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 4;
                bw.Write(pixels[i + 0]); // B
                bw.Write(pixels[i + 1]); // G
                bw.Write(pixels[i + 2]); // R
                bw.Write(pixels[i + 3]); // A
            }
        }

        // AND mask (all zeros — alpha channel handles transparency)
        for (int y = 0; y < h; y++)
            for (int x = 0; x < maskRowBytes; x++)
                bw.Write((byte)0);

        bw.Flush();
        ms.Position = 0;
        return new Cursor(ms);
    }

    // ============ TOOL DRAWINGS (32x32) ============

    private static readonly Pen WhitePen1 = new(Brushes.White, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
    private static readonly Pen WhitePen2 = new(Brushes.White, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    private static void DrawPencil(DrawingContext dc)
    {
        // Pencil angled ~45°, tip at bottom-left
        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(new Point(4, 28), true, true);
            ctx.LineTo(new Point(2, 30), true, false);
            ctx.LineTo(new Point(6, 26), true, false);
            ctx.LineTo(new Point(24, 8), true, false);
            ctx.LineTo(new Point(28, 12), true, false);
            ctx.LineTo(new Point(10, 30), true, false);
        }
        dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1), body);

        // Tip
        dc.DrawLine(new Pen(Brushes.Gray, 1), new Point(2, 30), new Point(6, 26));
        // Eraser end
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0x80, 0x80)),
            new Pen(Brushes.Black, 0.5), new Rect(22, 6, 8, 8));
    }

    private static void DrawEraserTool(DrawingContext dc)
    {
        // Eraser block shape
        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(new Point(8, 8), true, true);
            ctx.LineTo(new Point(26, 8), true, false);
            ctx.LineTo(new Point(26, 24), true, false);
            ctx.LineTo(new Point(22, 28), true, false);
            ctx.LineTo(new Point(10, 28), true, false);
            ctx.LineTo(new Point(8, 24), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
            new Pen(Brushes.Black, 1.2), body);

        // Eraser tip (pink bottom)
        var tip = new StreamGeometry();
        using (var ctx = tip.Open())
        {
            ctx.BeginFigure(new Point(8, 20), true, true);
            ctx.LineTo(new Point(26, 20), true, false);
            ctx.LineTo(new Point(26, 24), true, false);
            ctx.LineTo(new Point(22, 28), true, false);
            ctx.LineTo(new Point(10, 28), true, false);
            ctx.LineTo(new Point(8, 24), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50)),
            new Pen(Brushes.Black, 0.8), tip);

        // Bottom line
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1.5),
            new Point(6, 30), new Point(26, 30));
    }

    private static void DrawCrosshair(DrawingContext dc)
    {
        var pen = new Pen(Brushes.White, 1.2) { DashStyle = DashStyles.Solid };
        var shadow = new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 2.5);

        // Shadow
        dc.DrawLine(shadow, new Point(16, 6), new Point(16, 12));
        dc.DrawLine(shadow, new Point(16, 20), new Point(16, 26));
        dc.DrawLine(shadow, new Point(6, 16), new Point(12, 16));
        dc.DrawLine(shadow, new Point(20, 16), new Point(26, 16));

        // White crosshair
        dc.DrawLine(pen, new Point(16, 6), new Point(16, 12));
        dc.DrawLine(pen, new Point(16, 20), new Point(16, 26));
        dc.DrawLine(pen, new Point(6, 16), new Point(12, 16));
        dc.DrawLine(pen, new Point(20, 16), new Point(26, 16));

        // Center dot
        dc.DrawEllipse(Brushes.White, null, new Point(16, 16), 1.5, 1.5);
    }

    private static void DrawLineTool(DrawingContext dc)
    {
        DrawCrosshair(dc);
        // Small line indicator bottom-right
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)), 2)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            new Point(22, 28), new Point(30, 22));
    }

    private static void DrawArrowTool(DrawingContext dc)
    {
        DrawCrosshair(dc);
        // Small arrow indicator bottom-right
        var blue = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        dc.DrawLine(new Pen(blue, 2) { StartLineCap = PenLineCap.Round },
            new Point(22, 30), new Point(29, 23));
        var arrow = new StreamGeometry();
        using (var ctx = arrow.Open())
        {
            ctx.BeginFigure(new Point(31, 21), true, true);
            ctx.LineTo(new Point(27, 23), true, false);
            ctx.LineTo(new Point(29, 25), true, false);
        }
        dc.DrawGeometry(blue, null, arrow);
    }

    private static void DrawRectTool(DrawingContext dc)
    {
        DrawCrosshair(dc);
        dc.DrawRectangle(null,
            new Pen(new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)), 1.5),
            new Rect(22, 22, 8, 8));
    }

    private static void DrawEllipseTool(DrawingContext dc)
    {
        DrawCrosshair(dc);
        dc.DrawEllipse(null,
            new Pen(new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5)), 1.5),
            new Point(26, 26), 5, 4);
    }

    private static void DrawMarkerTool(DrawingContext dc)
    {
        // Marker/highlighter shape
        var body = new StreamGeometry();
        using (var ctx = body.Open())
        {
            ctx.BeginFigure(new Point(4, 26), true, true);
            ctx.LineTo(new Point(2, 30), true, false);
            ctx.LineTo(new Point(10, 30), true, false);
            ctx.LineTo(new Point(28, 12), true, false);
            ctx.LineTo(new Point(24, 6), true, false);
        }
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(180, 0xFF, 0xFF, 0x00)),
            new Pen(Brushes.Black, 1), body);
    }

    private static void DrawBlurTool(DrawingContext dc)
    {
        DrawCrosshair(dc);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1.2)
        {
            DashStyle = new DashStyle(new double[] { 2, 1 }, 0)
        };
        dc.DrawRectangle(null, pen, new Rect(21, 21, 9, 9));
    }

    private static void DrawCheckStamp(DrawingContext dc)
    {
        var green = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0x4C, 0xAF, 0x50)),
            new Pen(green, 2), new Point(16, 16), 12, 12);
        dc.DrawLine(new Pen(green, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
            new Point(9, 16), new Point(14, 21));
        dc.DrawLine(new Pen(green, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round },
            new Point(14, 21), new Point(23, 10));
    }

    private static void DrawCrossStamp(DrawingContext dc)
    {
        var red = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36));
        dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 0xF4, 0x43, 0x36)),
            new Pen(red, 2), new Point(16, 16), 12, 12);
        var crossPen = new Pen(red, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(crossPen, new Point(10, 10), new Point(22, 22));
        dc.DrawLine(crossPen, new Point(22, 10), new Point(10, 22));
    }
}

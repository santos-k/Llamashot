using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace Llamashot.Core;

public record LineSeg(int X1, int Y1, int X2, int Y2)
{
    public int Length => Math.Max(Math.Abs(X2 - X1), Math.Abs(Y2 - Y1));
}

public class SegmentSet
{
    public List<LineSeg> Horizontal { get; } = new();
    public List<LineSeg> Vertical { get; } = new();
}

public static partial class FillSignDetector
{
    const int DarkThreshold = 128;

    static bool[,] Binarize(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var dark = new bool[w, h];
        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            unsafe
            {
                byte* p0 = (byte*)data.Scan0;
                for (int y = 0; y < h; y++)
                {
                    byte* row = p0 + y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                        int lum = (r * 299 + g * 587 + b * 114) / 1000;
                        dark[x, y] = lum < DarkThreshold;
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return dark;
    }

    public static SegmentSet ExtractSegments(Bitmap bmp, double minLengthFraction)
    {
        var dark = Binarize(bmp);
        int w = bmp.Width, h = bmp.Height;
        int minH = (int)(w * minLengthFraction);
        int minV = (int)(h * minLengthFraction);
        var set = new SegmentSet();
        for (int y = 0; y < h; y++)
        {
            int run = 0;
            for (int x = 0; x < w; x++)
            {
                if (dark[x, y]) run++;
                else { if (run >= minH) set.Horizontal.Add(new LineSeg(x - run, y, x - 1, y)); run = 0; }
            }
            if (run >= minH) set.Horizontal.Add(new LineSeg(w - run, y, w - 1, y));
        }
        for (int x = 0; x < w; x++)
        {
            int run = 0;
            for (int y = 0; y < h; y++)
            {
                if (dark[x, y]) run++;
                else { if (run >= minV) set.Vertical.Add(new LineSeg(x, y - run, x, y - 1)); run = 0; }
            }
            if (run >= minV) set.Vertical.Add(new LineSeg(x, h - run, x, h - 1));
        }
        return set;
    }
}

public enum RegionKind { Underline, Box, Checkbox, Comb }

public record DetectedRegion(RegionKind Kind, int X, int Y, int W, int H);

public static partial class FillSignDetector
{
    public static List<DetectedRegion> DetectRegions(Bitmap bmp)
    {
        var seg = ExtractSegments(bmp, 0.012);
        var regions = new List<DetectedRegion>();
        int tol = Math.Max(3, bmp.Width / 300);

        foreach (var top in seg.Horizontal)
        {
            foreach (var bot in seg.Horizontal)
            {
                if (bot.Y1 - top.Y1 < 8 || bot.Y1 - top.Y1 > bmp.Height / 4) continue;
                int xL = Math.Max(top.X1, bot.X1), xR = Math.Min(top.X2, bot.X2);
                if (xR - xL < 8) continue;
                bool left = HasVerticalNear(seg, xL, top.Y1, bot.Y1, tol);
                bool right = HasVerticalNear(seg, xR, top.Y1, bot.Y1, tol);
                if (!(left && right)) continue;
                int w = xR - xL, h = bot.Y1 - top.Y1;
                int seps = CountInteriorVerticals(seg, xL, xR, top.Y1, bot.Y1, tol);
                RegionKind kind;
                if (seps >= 3) kind = RegionKind.Comb;
                else if (Math.Abs(w - h) <= Math.Max(w, h) * 0.4 && w <= bmp.Width / 25) kind = RegionKind.Checkbox;
                else kind = RegionKind.Box;
                AddUnique(regions, new DetectedRegion(kind, xL, top.Y1, w, h), tol);
            }
        }
        foreach (var hs in seg.Horizontal)
        {
            if (hs.Length < bmp.Width * 0.06) continue;
            bool insideBox = regions.Exists(r => r.Y <= hs.Y1 + tol && r.Y + r.H >= hs.Y1 - tol &&
                                                 r.X <= hs.X1 + tol && r.X + r.W >= hs.X2 - tol);
            if (!insideBox)
                AddUnique(regions, new DetectedRegion(RegionKind.Underline, hs.X1, hs.Y1 - 20, hs.Length, 20), tol);
        }
        return regions;
    }

    static bool HasVerticalNear(SegmentSet seg, int x, int yTop, int yBot, int tol) =>
        seg.Vertical.Exists(v => Math.Abs(v.X1 - x) <= tol && v.Y1 <= yTop + tol && v.Y2 >= yBot - tol);

    static int CountInteriorVerticals(SegmentSet seg, int xL, int xR, int yTop, int yBot, int tol)
    {
        int c = 0;
        foreach (var v in seg.Vertical)
            if (v.X1 > xL + tol && v.X1 < xR - tol && v.Y1 <= yTop + tol && v.Y2 >= yBot - tol) c++;
        return c;
    }

    static void AddUnique(List<DetectedRegion> list, DetectedRegion r, int tol)
    {
        if (!list.Exists(e => e.Kind == r.Kind && Math.Abs(e.X - r.X) <= tol &&
                              Math.Abs(e.Y - r.Y) <= tol && Math.Abs(e.W - r.W) <= tol))
            list.Add(r);
    }
}

/// <summary>A best-effort estimate of the text style under a point on the page.</summary>
public record FontSample(double FontSizePt, string ColorHex, bool Bold, bool Italic);

public static partial class FillSignDetector
{
    /// <summary>
    /// Samples the rendered page around a pixel to estimate the document text's font size, ink color
    /// and (best-effort) bold/italic, so a placed text field can match the surrounding text.
    /// Returns null when no text is found near the point. <paramref name="px"/>/<paramref name="py"/> are
    /// pixel coordinates in the bitmap rendered at <paramref name="dpi"/>.
    /// </summary>
    public static unsafe FontSample? DetectFontAt(Bitmap bmp, int px, int py, int dpi)
    {
        int w = bmp.Width, h = bmp.Height;
        if (w == 0 || h == 0) return null;
        px = Math.Clamp(px, 0, w - 1); py = Math.Clamp(py, 0, h - 1);

        int wx = Math.Max(24, w / 30);                 // horizontal sampling half-width
        int vy = Math.Max(20, (int)(dpi * 0.55));      // vertical search half-height
        int x0 = Math.Max(0, px - wx), x1 = Math.Min(w - 1, px + wx);
        int y0 = Math.Max(0, py - vy), y1 = Math.Min(h - 1, py + vy);
        int bandW = x1 - x0 + 1;
        int minCount = Math.Max(2, bandW / 8);

        var rect = new System.Drawing.Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte* p0 = (byte*)data.Scan0;

            int Count(int y)
            {
                int n = 0; byte* row = p0 + y * stride;
                for (int x = x0; x <= x1; x++)
                {
                    byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                    if ((r * 299 + g * 587 + b * 114) / 1000 < DarkThreshold) n++;
                }
                return n;
            }

            // find the nearest text row to the click, then grow up/down through the run (tolerate 2-row gaps)
            int seed = -1;
            for (int d = 0; d <= vy; d++)
            {
                if (py - d >= y0 && Count(py - d) >= minCount) { seed = py - d; break; }
                if (py + d <= y1 && Count(py + d) >= minCount) { seed = py + d; break; }
            }
            if (seed < 0) return null;

            int top = seed, bottom = seed;
            for (int y = seed - 1, g2 = 0; y >= y0; y--) { if (Count(y) >= minCount) { top = y; g2 = 0; } else if (++g2 > 2) break; }
            for (int y = seed + 1, g2 = 0; y <= y1; y++) { if (Count(y) >= minCount) { bottom = y; g2 = 0; } else if (++g2 > 2) break; }
            int heightPx = bottom - top + 1;
            if (heightPx < 3) return null;

            // ink color (avg of dark pixels), density (bold), and slant (italic)
            long rs = 0, gs = 0, bs = 0, dark = 0;
            double topCx = 0, botCx = 0; long topN = 0, botN = 0;
            int third = Math.Max(1, heightPx / 3);
            for (int y = top; y <= bottom; y++)
            {
                byte* row = p0 + y * stride;
                for (int x = x0; x <= x1; x++)
                {
                    byte b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                    if ((r * 299 + g * 587 + b * 114) / 1000 >= DarkThreshold) continue;
                    rs += r; gs += g; bs += b; dark++;
                    if (y < top + third) { topCx += x; topN++; }
                    else if (y > bottom - third) { botCx += x; botN++; }
                }
            }
            if (dark == 0) return null;

            string hex = string.Format("#{0:X2}{1:X2}{2:X2}", rs / dark, gs / dark, bs / dark);

            double density = (double)dark / (heightPx * (double)bandW);
            bool bold = density > 0.30;

            bool italic = false;
            if (topN > 5 && botN > 5)
            {
                double slant = (topCx / topN) - (botCx / botN); // italic leans right: top centroid right of bottom
                italic = slant > heightPx * 0.12;
            }

            // run height (cap-top..descender) is ~0.7 em for typical text → estimate em, then points
            double emPx = heightPx / 0.7;
            double sizePt = Math.Clamp(Math.Round(FillSignGeometry.PixelsToPoints(emPx, dpi)), 6, 96);

            return new FontSample(sizePt, hex, bold, italic);
        }
        finally { bmp.UnlockBits(data); }
    }
}

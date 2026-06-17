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

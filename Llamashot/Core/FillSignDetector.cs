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

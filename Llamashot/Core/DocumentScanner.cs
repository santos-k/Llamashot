using System;

namespace Llamashot.Core;

/// <summary>
/// Document scanner: warps a four-corner quadrilateral selection from a skewed photo into a
/// flat, perfectly rectangular image (perspective / keystone correction). Supports automatic
/// edge detection and scan-style enhancement (magic colour, grayscale, black-and-white).
/// All operations work on BGRA32 pixel buffers.
/// </summary>
public static class DocumentScanner
{
    public enum Enhance { Original, Magic, Grayscale, BlackWhite }

    /// <summary>
    /// Perspective-warps the quadrilateral (image-space corners, order TL, TR, BR, BL) from the
    /// source buffer into an <paramref name="ow"/>×<paramref name="oh"/> rectangle using a
    /// projective homography with bilinear sampling, then applies the chosen enhancement.
    /// </summary>
    public static byte[] Warp(byte[] src, int sw, int sh, (double x, double y)[] quad, int ow, int oh, Enhance enhance)
    {
        // Homography mapping the output unit square -> source quad (Heckbert square-to-quad).
        double x0 = quad[0].x, y0 = quad[0].y; // (0,0) TL
        double x1 = quad[1].x, y1 = quad[1].y; // (1,0) TR
        double x2 = quad[2].x, y2 = quad[2].y; // (1,1) BR
        double x3 = quad[3].x, y3 = quad[3].y; // (0,1) BL

        double dx1 = x1 - x2, dx2 = x3 - x2, dx3 = x0 - x1 + x2 - x3;
        double dy1 = y1 - y2, dy2 = y3 - y2, dy3 = y0 - y1 + y2 - y3;
        double den = dx1 * dy2 - dx2 * dy1;
        double g, h;
        if (Math.Abs(den) < 1e-9) { g = 0; h = 0; }
        else { g = (dx3 * dy2 - dx2 * dy3) / den; h = (dx1 * dy3 - dx3 * dy1) / den; }
        double a = x1 - x0 + g * x1, b = x3 - x0 + h * x3, c = x0;
        double d = y1 - y0 + g * y1, e = y3 - y0 + h * y3, f = y0;

        var dst = new byte[ow * oh * 4];
        for (int oy = 0; oy < oh; oy++)
        {
            double v = oh > 1 ? (double)oy / (oh - 1) : 0;
            for (int ox = 0; ox < ow; ox++)
            {
                double u = ow > 1 ? (double)ox / (ow - 1) : 0;
                double w = g * u + h * v + 1;
                double sx = (a * u + b * v + c) / w;
                double sy = (d * u + e * v + f) / w;
                SampleBilinear(src, sw, sh, sx, sy, dst, (oy * ow + ox) * 4);
            }
        }

        switch (enhance)
        {
            case Enhance.Magic: MagicColor(dst, ow, oh); break;
            case Enhance.Grayscale: ToGray(dst); break;
            case Enhance.BlackWhite: AdaptiveThreshold(dst, ow, oh); break;
        }
        return dst;
    }

    private static void SampleBilinear(byte[] src, int sw, int sh, double sx, double sy, byte[] dst, int di)
    {
        sx = Math.Clamp(sx, 0, sw - 1.0001);
        sy = Math.Clamp(sy, 0, sh - 1.0001);
        int x0 = (int)sx, y0 = (int)sy;
        int x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
        double fx = sx - x0, fy = sy - y0;
        int i00 = (y0 * sw + x0) * 4, i10 = (y0 * sw + x1) * 4;
        int i01 = (y1 * sw + x0) * 4, i11 = (y1 * sw + x1) * 4;
        for (int ch = 0; ch < 4; ch++)
        {
            double top = src[i00 + ch] * (1 - fx) + src[i10 + ch] * fx;
            double bot = src[i01 + ch] * (1 - fx) + src[i11 + ch] * fx;
            dst[di + ch] = (byte)Math.Clamp(top * (1 - fy) + bot * fy + 0.5, 0, 255);
        }
        dst[di + 3] = 255;
    }

    // =====================================================================
    //  Enhancement
    // =====================================================================

    /// <summary>Per-channel contrast stretch (2nd–98th percentile) + mild saturation lift — the
    /// classic "scan/enhance" look that brightens paper and deepens ink.</summary>
    private static void MagicColor(byte[] p, int w, int h)
    {
        int n = w * h;
        for (int ch = 0; ch < 3; ch++)
        {
            var hist = new int[256];
            for (int i = 0; i < n; i++) hist[p[i * 4 + ch]]++;
            int lo = Percentile(hist, n, 0.02), hi = Percentile(hist, n, 0.98);
            if (hi <= lo) continue;
            double scale = 255.0 / (hi - lo);
            for (int i = 0; i < n; i++)
            {
                int idx = i * 4 + ch;
                p[idx] = (byte)Math.Clamp((p[idx] - lo) * scale + 0.5, 0, 255);
            }
        }
    }

    private static int Percentile(int[] hist, int total, double frac)
    {
        int target = (int)(total * frac), acc = 0;
        for (int v = 0; v < 256; v++) { acc += hist[v]; if (acc >= target) return v; }
        return 255;
    }

    private static void ToGray(byte[] p)
    {
        for (int i = 0; i < p.Length; i += 4)
        {
            byte y = (byte)(p[i + 2] * 0.299 + p[i + 1] * 0.587 + p[i] * 0.114 + 0.5);
            p[i] = p[i + 1] = p[i + 2] = y;
        }
    }

    /// <summary>Adaptive (local-mean) threshold — turns a document photo into clean black text on
    /// white paper, robust to uneven lighting and shadows.</summary>
    private static void AdaptiveThreshold(byte[] p, int w, int h)
    {
        int n = w * h;
        var lum = new double[n];
        for (int i = 0; i < n; i++)
            lum[i] = p[i * 4 + 2] * 0.299 + p[i * 4 + 1] * 0.587 + p[i * 4] * 0.114;

        // Integral image for O(1) window means.
        var integ = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double rowSum = 0;
            for (int x = 0; x < w; x++)
            {
                rowSum += lum[y * w + x];
                integ[(y + 1) * (w + 1) + (x + 1)] = integ[y * (w + 1) + (x + 1)] + rowSum;
            }
        }

        int rad = Math.Max(8, Math.Min(w, h) / 24); // window half-size
        const double t = 0.85;                       // keep slightly below local mean
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int x1 = Math.Max(0, x - rad), y1 = Math.Max(0, y - rad);
                int x2 = Math.Min(w - 1, x + rad), y2 = Math.Min(h - 1, y + rad);
                int area = (x2 - x1 + 1) * (y2 - y1 + 1);
                double sum = integ[(y2 + 1) * (w + 1) + (x2 + 1)] - integ[y1 * (w + 1) + (x2 + 1)]
                           - integ[(y2 + 1) * (w + 1) + x1] + integ[y1 * (w + 1) + x1];
                double mean = sum / area;
                byte val = lum[y * w + x] < mean * t ? (byte)0 : (byte)255;
                int idx = (y * w + x) * 4;
                p[idx] = p[idx + 1] = p[idx + 2] = val;
            }
    }

    // =====================================================================
    //  Auto edge detection
    // =====================================================================

    /// <summary>
    /// Best-effort automatic detection of the document corners (TL, TR, BR, BL) by segmenting the
    /// page from the background (sampled at the photo's corners) and taking the extreme points of
    /// the largest region. Returns null when no confident rectangular page is found.
    /// </summary>
    public static (double x, double y)[]? AutoDetectCorners(byte[] bgra, int w, int h)
    {
        // Work on a downscaled copy for speed.
        int maxDim = 480;
        double f = Math.Min(1.0, (double)maxDim / Math.Max(w, h));
        int dw = Math.Max(1, (int)(w * f)), dh = Math.Max(1, (int)(h * f));
        var sb = new byte[dw * dh * 3]; // RGB small
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                int sx = (int)(x / f), sy = (int)(y / f);
                int si = (sy * w + sx) * 4, di = (y * dw + x) * 3;
                sb[di] = bgra[si + 2]; sb[di + 1] = bgra[si + 1]; sb[di + 2] = bgra[si];
            }

        // Background colour = average of the four 8px corner patches.
        long br = 0, bg = 0, bb = 0; int cnt = 0; int q = 8;
        foreach (var (cx, cy) in new[] { (0, 0), (dw - q, 0), (0, dh - q), (dw - q, dh - q) })
            for (int y = 0; y < q; y++)
                for (int x = 0; x < q; x++)
                {
                    int di = ((cy + y) * dw + (cx + x)) * 3;
                    br += sb[di]; bg += sb[di + 1]; bb += sb[di + 2]; cnt++;
                }
        double mr = (double)br / cnt, mg = (double)bg / cnt, mb = (double)bb / cnt;

        // Foreground mask = pixels far from the background colour.
        const double thr = 48 * 48 * 3; // squared distance
        var fg = new bool[dw * dh];
        int fgCount = 0;
        for (int i = 0; i < dw * dh; i++)
        {
            double dr = sb[i * 3] - mr, dg = sb[i * 3 + 1] - mg, db = sb[i * 3 + 2] - mb;
            if (dr * dr + dg * dg + db * db > thr) { fg[i] = true; fgCount++; }
        }
        if (fgCount < dw * dh * 0.10) return null; // too little — give up

        // Largest 4-connected component via BFS.
        var seen = new bool[dw * dh];
        var stack = new int[dw * dh];
        int bestStart = -1, bestSize = 0;
        var bestPixels = new System.Collections.Generic.List<int>();
        var cur = new System.Collections.Generic.List<int>();
        for (int s = 0; s < dw * dh; s++)
        {
            if (!fg[s] || seen[s]) continue;
            int sp = 0; stack[sp++] = s; seen[s] = true; cur.Clear();
            while (sp > 0)
            {
                int idx = stack[--sp]; cur.Add(idx);
                int cx = idx % dw, cy = idx / dw;
                if (cx > 0 && fg[idx - 1] && !seen[idx - 1]) { seen[idx - 1] = true; stack[sp++] = idx - 1; }
                if (cx < dw - 1 && fg[idx + 1] && !seen[idx + 1]) { seen[idx + 1] = true; stack[sp++] = idx + 1; }
                if (cy > 0 && fg[idx - dw] && !seen[idx - dw]) { seen[idx - dw] = true; stack[sp++] = idx - dw; }
                if (cy < dh - 1 && fg[idx + dw] && !seen[idx + dw]) { seen[idx + dw] = true; stack[sp++] = idx + dw; }
            }
            if (cur.Count > bestSize) { bestSize = cur.Count; bestPixels = new System.Collections.Generic.List<int>(cur); bestStart = s; }
        }
        if (bestStart < 0 || bestSize < dw * dh * 0.10) return null;

        // Extreme points of the region give the rotated-rectangle corners.
        double sMin = double.MaxValue, sMax = double.MinValue, dMin = double.MaxValue, dMax = double.MinValue;
        int tlx = 0, tly = 0, brx = 0, bry = 0, trx = 0, tryy = 0, blx = 0, bly = 0;
        foreach (int idx in bestPixels)
        {
            int x = idx % dw, y = idx / dw;
            int sum = x + y, dif = x - y;
            if (sum < sMin) { sMin = sum; tlx = x; tly = y; }
            if (sum > sMax) { sMax = sum; brx = x; bry = y; }
            if (dif > dMax) { dMax = dif; trx = x; tryy = y; }
            if (dif < dMin) { dMin = dif; blx = x; bly = y; }
        }

        // Reject degenerate detections (corners collapsed together).
        double minSep = Math.Min(dw, dh) * 0.15;
        if (Dist(tlx, tly, trx, tryy) < minSep || Dist(tlx, tly, blx, bly) < minSep) return null;

        double inv = 1.0 / f;
        return new[]
        {
            ((double)tlx * inv, (double)tly * inv),
            ((double)trx * inv, (double)tryy * inv),
            ((double)brx * inv, (double)bry * inv),
            ((double)blx * inv, (double)bly * inv),
        };
    }

    private static double Dist(double ax, double ay, double bx, double by)
        => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
}

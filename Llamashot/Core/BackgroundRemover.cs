namespace Llamashot.Core;

/// <summary>
/// Pure pixel algorithms for the Remove-Background tool. Buffers are 32-bit BGRA
/// (the WPF <c>Bgra32</c> layout): index = (y*w + x) * 4, channels B,G,R,A.
/// All methods mutate alpha (or colour) in place unless they return a new buffer.
/// </summary>
public static class BackgroundRemover
{
    private static double TolDist(int tolerance) => tolerance / 100.0 * 180.0; // 0–100 slider → 0–180 colour distance

    private static double Dist2(byte[] px, int i, int b, int g, int r)
    {
        double db = px[i] - b, dg = px[i + 1] - g, dr = px[i + 2] - r;
        return db * db + dg * dg + dr * dr;
    }

    /// <summary>Squared distance from pixel i to the nearest colour in the palette.</summary>
    private static double MinDist2(byte[] px, int i, List<(int b, int g, int r)> pal)
    {
        double best = double.MaxValue;
        for (int k = 0; k < pal.Count; k++)
        {
            double db = px[i] - pal[k].b, dg = px[i + 1] - pal[k].g, dr = px[i + 2] - pal[k].r;
            double d = db * db + dg * dg + dr * dr;
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>
    /// Builds a small palette of representative background colours by sampling the image border and
    /// greedily clustering — so a multi-region background (e.g. sky + grass + path) clears cleanly
    /// instead of being averaged into one muddy colour that matches nothing.
    /// </summary>
    private static List<(int b, int g, int r)> BorderPalette(byte[] px, int w, int h, int maxColors = 6)
    {
        double mergeT = TolDist(16); double mergeT2 = mergeT * mergeT;
        var sums = new List<(double b, double g, double r, long n)>();

        void Add(int i)
        {
            int b = px[i], g = px[i + 1], r = px[i + 2];
            int best = -1; double bd = double.MaxValue;
            for (int k = 0; k < sums.Count; k++)
            {
                double db = b - sums[k].b / sums[k].n, dg = g - sums[k].g / sums[k].n, dr = r - sums[k].r / sums[k].n;
                double d = db * db + dg * dg + dr * dr;
                if (d < bd) { bd = d; best = k; }
            }
            if (best >= 0 && (bd <= mergeT2 || sums.Count >= maxColors))
            {
                var s = sums[best];
                sums[best] = (s.b + b, s.g + g, s.r + r, s.n + 1);
            }
            else sums.Add((b, g, r, 1));
        }

        int step = Math.Max(1, (w + h) / 600);
        for (int x = 0; x < w; x += step) { Add(x * 4); Add(((h - 1) * w + x) * 4); }
        for (int y = 0; y < h; y += step) { Add((y * w) * 4); Add((y * w + w - 1) * 4); }

        var pal = new List<(int b, int g, int r)>();
        foreach (var s in sums) pal.Add(((int)(s.b / s.n), (int)(s.g / s.n), (int)(s.r / s.n)));
        if (pal.Count == 0) pal.Add((0, 0, 0));
        return pal;
    }

    /// <summary>
    /// Region-grow from <paramref name="seeds"/>, clearing alpha. A neighbour joins if it's within
    /// <paramref name="tolerance"/> of the background reference, or (gradient follow) within a tighter
    /// tolerance of the pixel it spread from — so soft / slightly graded backgrounds clear in one pass.
    /// </summary>
    private static void Flood(byte[] px, int w, int h, IEnumerable<int> seeds,
        List<(int b, int g, int r)> palette, int tolerance)
    {
        double t = TolDist(tolerance); double t2 = t * t;
        var visited = new bool[w * h];
        var stack = new Stack<int>();

        foreach (int s in seeds)
        {
            if (s < 0 || s >= w * h || visited[s]) continue;
            int i = s * 4;
            if (px[i + 3] == 0) { visited[s] = true; continue; }
            if (MinDist2(px, i, palette) <= t2) { visited[s] = true; stack.Push(s); }
        }

        while (stack.Count > 0)
        {
            int p = stack.Pop();
            px[p * 4 + 3] = 0;
            int x = p % w, y = p / w;

            void Visit(int q)
            {
                if (q < 0 || q >= w * h || visited[q]) return;
                int j = q * 4;
                if (px[j + 3] == 0) { visited[q] = true; return; }
                if (MinDist2(px, j, palette) <= t2) { visited[q] = true; stack.Push(q); }
            }

            if (x > 0) Visit(p - 1);
            if (x < w - 1) Visit(p + 1);
            if (y > 0) Visit(p - w);
            if (y < h - 1) Visit(p + w);
        }
    }

    /// <summary>Clear edge pixels (those touching transparency) that are within an expanded tolerance
    /// of the background colour — removes the anti-aliased colour halo left along a cut edge.</summary>
    private static void Defringe(byte[] px, int w, int h, List<(int b, int g, int r)> palette, int tolerance, int passes)
    {
        double tHi = TolDist(Math.Min(100, (int)(tolerance * 1.6))); double tHi2 = tHi * tHi;
        for (int pass = 0; pass < passes; pass++)
        {
            var clear = new List<int>();
            for (int p = 0; p < w * h; p++)
            {
                int i = p * 4;
                if (px[i + 3] == 0) continue;
                int x = p % w, y = p / w;
                bool edge = (x > 0 && px[(p - 1) * 4 + 3] == 0) || (x < w - 1 && px[(p + 1) * 4 + 3] == 0)
                          || (y > 0 && px[(p - w) * 4 + 3] == 0) || (y < h - 1 && px[(p + w) * 4 + 3] == 0);
                if (edge && MinDist2(px, i, palette) <= tHi2) clear.Add(p);
            }
            if (clear.Count == 0) break;
            foreach (int p in clear) px[p * 4 + 3] = 0;
        }
    }

    /// <summary>Shave <paramref name="iters"/> pixels off every cut edge — kills thin bright fringes/specks.</summary>
    private static void Erode(byte[] px, int w, int h, int iters)
    {
        for (int it = 0; it < iters; it++)
        {
            var clear = new List<int>();
            for (int p = 0; p < w * h; p++)
            {
                int i = p * 4;
                if (px[i + 3] == 0) continue;
                int x = p % w, y = p / w;
                bool edge = (x > 0 && px[(p - 1) * 4 + 3] == 0) || (x < w - 1 && px[(p + 1) * 4 + 3] == 0)
                          || (y > 0 && px[(p - w) * 4 + 3] == 0) || (y < h - 1 && px[(p + w) * 4 + 3] == 0);
                if (edge) clear.Add(p);
            }
            if (clear.Count == 0) break;
            foreach (int p in clear) px[p * 4 + 3] = 0;
        }
    }

    /// <summary>
    /// Auto-detect: clear the background connected to the image border, then defringe and shave the
    /// edge so the cut-out is clean. Best on fairly uniform / solid backgrounds.
    /// </summary>
    public static void AutoRemove(byte[] px, int w, int h, int tolerance)
    {
        if (w <= 0 || h <= 0 || px.Length < w * h * 4) return;
        var palette = BorderPalette(px, w, h);

        var seeds = new List<int>(2 * (w + h));
        for (int x = 0; x < w; x++) { seeds.Add(x); seeds.Add((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { seeds.Add(y * w); seeds.Add(y * w + w - 1); }

        // Palette flood (no gradient-follow — that walks across shaded subjects and eats them):
        // a pixel clears only if it's close to one of the sampled background colours.
        Flood(px, w, h, seeds, palette, tolerance);
        Defringe(px, w, h, palette, tolerance, passes: 2);
        Erode(px, w, h, 1);
    }

    /// <summary>Magic wand: clear the region connected to (sx,sy) within tolerance of the clicked colour,
    /// then defringe + shave so edges are clean.</summary>
    public static void MagicWand(byte[] px, int w, int h, int sx, int sy, int tolerance)
    {
        if (sx < 0 || sy < 0 || sx >= w || sy >= h) return;
        int si = (sy * w + sx) * 4;
        if (px[si + 3] == 0) return;
        int b0 = px[si], g0 = px[si + 1], r0 = px[si + 2];
        var pal = new List<(int b, int g, int r)> { (b0, g0, r0) };

        Flood(px, w, h, new[] { sy * w + sx }, pal, tolerance);
        Defringe(px, w, h, pal, tolerance, passes: 2);
        Erode(px, w, h, 1);
    }

    /// <summary>Erase (alpha→0) or restore (copy colour+alpha from <paramref name="orig"/>) inside a circular brush.</summary>
    public static (int x, int y, int w, int h) Brush(byte[] px, byte[]? orig, int w, int h, int cx, int cy, int radius, bool erase)
    {
        int r2 = radius * radius;
        int x0 = Math.Max(0, cx - radius), x1 = Math.Min(w - 1, cx + radius);
        int y0 = Math.Max(0, cy - radius), y1 = Math.Min(h - 1, cy + radius);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;
                int i = (y * w + x) * 4;
                if (erase) px[i + 3] = 0;
                else if (orig != null) { px[i] = orig[i]; px[i + 1] = orig[i + 1]; px[i + 2] = orig[i + 2]; px[i + 3] = orig[i + 3]; }
            }
        return (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>Box-blur the colour channels of visible pixels inside a circular brush (alpha untouched).</summary>
    public static (int x, int y, int w, int h) BlurAt(byte[] px, int w, int h, int cx, int cy, int radius, int strength)
    {
        int x0 = Math.Max(0, cx - radius), x1 = Math.Min(w - 1, cx + radius);
        int y0 = Math.Max(0, cy - radius), y1 = Math.Min(h - 1, cy + radius);
        var src = (byte[])px.Clone();
        int r2 = radius * radius;
        int k = Math.Max(1, strength);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - cx, dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;
                int i = (y * w + x) * 4;
                if (px[i + 3] == 0) continue;
                long sb = 0, sg = 0, sr = 0; int n = 0;
                for (int yy = Math.Max(0, y - k); yy <= Math.Min(h - 1, y + k); yy++)
                    for (int xx = Math.Max(0, x - k); xx <= Math.Min(w - 1, x + k); xx++)
                    {
                        int j = (yy * w + xx) * 4;
                        if (src[j + 3] == 0) continue;
                        sb += src[j]; sg += src[j + 1]; sr += src[j + 2]; n++;
                    }
                if (n > 0) { px[i] = (byte)(sb / n); px[i + 1] = (byte)(sg / n); px[i + 2] = (byte)(sr / n); }
            }
        return (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>
    /// Feathers the alpha channel with a separable box blur (radius = <paramref name="radius"/>) so the
    /// cut-out edge is smooth/anti-aliased instead of a hard jagged line. Interior and far-exterior are
    /// unaffected (blurring all-255 stays 255, all-0 stays 0); only the boundary ramps.
    /// </summary>
    public static void SmoothAlpha(byte[] px, int w, int h, int radius)
    {
        if (radius <= 0 || w <= 0 || h <= 0) return;
        int win = radius * 2 + 1;
        var a = new byte[w * h];
        for (int p = 0; p < w * h; p++) a[p] = px[p * 4 + 3];
        var tmp = new int[w * h];

        // Horizontal pass (sliding window).
        for (int y = 0; y < h; y++)
        {
            int row = y * w, sum = 0;
            for (int k = -radius; k <= radius; k++) sum += a[row + Math.Clamp(k, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = sum / win;
                int addX = Math.Clamp(x + radius + 1, 0, w - 1), remX = Math.Clamp(x - radius, 0, w - 1);
                sum += a[row + addX] - a[row + remX];
            }
        }
        // Vertical pass.
        for (int x = 0; x < w; x++)
        {
            int sum = 0;
            for (int k = -radius; k <= radius; k++) sum += tmp[Math.Clamp(k, 0, h - 1) * w + x];
            for (int y = 0; y < h; y++)
            {
                px[(y * w + x) * 4 + 3] = (byte)(sum / win);
                int addY = Math.Clamp(y + radius + 1, 0, h - 1), remY = Math.Clamp(y - radius, 0, h - 1);
                sum += tmp[addY * w + x] - tmp[remY * w + x];
            }
        }
    }

    /// <summary>Separable box blur over the RGB channels (alpha left untouched). Used for
    /// background-blur compositing.</summary>
    public static void BoxBlur(byte[] px, int w, int h, int radius)
    {
        if (radius <= 0 || w <= 0 || h <= 0) return;
        int win = radius * 2 + 1;
        var tmp = new byte[px.Length];
        // Horizontal pass.
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int ch = 0; ch < 3; ch++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++) sum += px[(row + Math.Clamp(k, 0, w - 1)) * 4 + ch];
                for (int x = 0; x < w; x++)
                {
                    tmp[(row + x) * 4 + ch] = (byte)(sum / win);
                    int addX = Math.Clamp(x + radius + 1, 0, w - 1), remX = Math.Clamp(x - radius, 0, w - 1);
                    sum += px[(row + addX) * 4 + ch] - px[(row + remX) * 4 + ch];
                }
            }
        }
        // Vertical pass.
        for (int x = 0; x < w; x++)
            for (int ch = 0; ch < 3; ch++)
            {
                int sum = 0;
                for (int k = -radius; k <= radius; k++) sum += tmp[(Math.Clamp(k, 0, h - 1) * w + x) * 4 + ch];
                for (int y = 0; y < h; y++)
                {
                    px[(y * w + x) * 4 + ch] = (byte)(sum / win);
                    int addY = Math.Clamp(y + radius + 1, 0, h - 1), remY = Math.Clamp(y - radius, 0, h - 1);
                    sum += tmp[(addY * w + x) * 4 + ch] - tmp[(remY * w + x) * 4 + ch];
                }
            }
    }

    /// <summary>Composite the cut-out over a solid colour (for JPEG export, which has no alpha).</summary>
    public static byte[] FlattenOnto(byte[] px, byte bgB, byte bgG, byte bgR)
    {
        var outp = new byte[px.Length];
        for (int i = 0; i < px.Length; i += 4)
        {
            double a = px[i + 3] / 255.0, ia = 1 - a;
            outp[i] = (byte)(px[i] * a + bgB * ia);
            outp[i + 1] = (byte)(px[i + 1] * a + bgG * ia);
            outp[i + 2] = (byte)(px[i + 2] * a + bgR * ia);
            outp[i + 3] = 255;
        }
        return outp;
    }
}

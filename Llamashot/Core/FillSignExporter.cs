using System.Collections.Generic;
using System.IO;
using Llamashot.Models;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Llamashot.Core;

public static class FillSignExporter
{
    static bool _resolverSet;

    static void EnsureResolver()
    {
        if (_resolverSet) return;
        // Guard against PDFsharp's "resolver already used" exception when running in shared process.
        try
        {
            GlobalFontSettings.FontResolver = new FillSignFontResolver();
        }
        catch (System.InvalidOperationException) { /* already used — proceed with whatever is set */ }
        _resolverSet = true;
    }

    static XColor Color(string hex)
    {
        var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        return XColor.FromArgb(c.A, c.R, c.G, c.B);
    }

    /// <summary>Approach B: inject vector content into the original PDF. Throws if PDFsharp cannot open it.</summary>
    static void ExportVector(string srcPdf, List<FillElement> elements, string outPath)
    {
        using var doc = PdfReader.Open(srcPdf, PdfDocumentOpenMode.Modify);
        doc.Options.CompressContentStreams = false; // keep tokens visible for verification

        var byPage = new Dictionary<int, List<FillElement>>();
        foreach (var e in elements)
        {
            if (!byPage.TryGetValue(e.Page, out var l)) { l = new(); byPage[e.Page] = l; }
            l.Add(e);
        }

        foreach (var kv in byPage)
        {
            int pageIdx = kv.Key;
            if (pageIdx < 0 || pageIdx >= doc.Pages.Count) continue;
            var page = doc.Pages[pageIdx];
            using var gfx = XGraphics.FromPdfPage(page);
            foreach (var e in kv.Value) DrawElement(gfx, e);
        }
        doc.Save(outPath);
    }

    static void DrawElement(XGraphics gfx, FillElement e)
    {
        XGraphicsState? st = null;
        if (e.Rotation != 0)
        {
            double cx = e.X + e.Width / 2, cy = e.Y + e.Height / 2;
            st = gfx.Save();
            gfx.TranslateTransform(cx, cy);
            gfx.RotateTransform(e.Rotation);
            gfx.TranslateTransform(-cx, -cy);
        }
        switch (e.Type)
        {
            case FillElementType.Text:
            case FillElementType.DateTime:
                DrawText(gfx, e, false);
                break;
            case FillElementType.Check:
                DrawText(gfx, e, true);
                break;
            case FillElementType.Signature:
            case FillElementType.Stamp:
            case FillElementType.Blur: // blur is pre-rendered to an image (with opacity baked in) before export
                if (e.ImagePath != null && File.Exists(e.ImagePath))
                    gfx.DrawImage(XImage.FromFile(e.ImagePath), e.X, e.Y, e.Width, e.Height);
                break;
            case FillElementType.Redact:
                var c = Color(e.ColorHex);
                if (e.Opacity < 100) c = XColor.FromArgb((byte)(255 * e.Opacity / 100.0), c.R, c.G, c.B);
                gfx.DrawRectangle(new XSolidBrush(c), new XRect(e.X, e.Y, e.Width, e.Height));
                break;
            case FillElementType.Draw:
                DrawShape(gfx, e);
                break;
        }
        if (st != null) gfx.Restore(st);
    }

    static void DrawShape(XGraphics gfx, FillElement e)
    {
        var col = Color(e.ColorHex);
        bool highlight = e.Shape == "highlight";
        double width = e.StrokeWidth <= 0 ? 2 : e.StrokeWidth;
        if (highlight)
        {
            col = XColor.FromArgb((byte)(255 * (e.Opacity < 100 ? e.Opacity : 40) / 100.0), col.R, col.G, col.B);
        }
        else if (e.Opacity < 100)
        {
            col = XColor.FromArgb((byte)(255 * e.Opacity / 100.0), col.R, col.G, col.B);
        }
        var pen = new XPen(col, width) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };

        switch (e.Shape)
        {
            case "rect":
                gfx.DrawRectangle(pen, e.X, e.Y, e.Width, e.Height);
                break;
            case "ellipse":
                gfx.DrawEllipse(pen, e.X, e.Y, e.Width, e.Height);
                break;
            case "line":
                if (e.Points.Count >= 2)
                    gfx.DrawLine(pen, e.X + e.Points[0].X, e.Y + e.Points[0].Y, e.X + e.Points[1].X, e.Y + e.Points[1].Y);
                break;
            case "arrow":
                if (e.Points.Count >= 2)
                {
                    double sx = e.X + e.Points[0].X, sy = e.Y + e.Points[0].Y;
                    double tx = e.X + e.Points[1].X, ty = e.Y + e.Points[1].Y;
                    gfx.DrawLine(pen, sx, sy, tx, ty);
                    foreach (var (hx, hy) in ArrowHead(sx, sy, tx, ty, width))
                        gfx.DrawLine(pen, tx, ty, hx, hy);
                }
                break;
            default: // pen / highlight freehand
                if (e.Points.Count >= 2)
                {
                    var pts = new XPoint[e.Points.Count];
                    for (int i = 0; i < e.Points.Count; i++) pts[i] = new XPoint(e.X + e.Points[i].X, e.Y + e.Points[i].Y);
                    gfx.DrawLines(pen, pts);
                }
                break;
        }
    }

    static IEnumerable<(double x, double y)> ArrowHead(double sx, double sy, double tx, double ty, double strokeWidth)
    {
        double dx = tx - sx, dy = ty - sy;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001) yield break;
        dx /= len; dy /= len;
        double head = System.Math.Max(8, strokeWidth * 3.5);
        const double ang = 25 * System.Math.PI / 180.0;
        double cos = System.Math.Cos(ang), sin = System.Math.Sin(ang);
        // rotate the reversed direction by ±angle
        yield return (tx - head * (dx * cos - dy * sin), ty - head * (dx * sin + dy * cos));
        yield return (tx - head * (dx * cos + dy * sin), ty - head * (-dx * sin + dy * cos));
    }

    static void DrawText(XGraphics gfx, FillElement e, bool glyph)
    {
        var style = (e.Bold ? XFontStyleEx.Bold : 0) | (e.Italic ? XFontStyleEx.Italic : 0)
                  | (e.Underline ? XFontStyleEx.Underline : 0);
        var font = new XFont(e.FontFamily, e.FontSize, style);
        var brush = new XSolidBrush(Color(e.ColorHex));
        string text = glyph ? (string.IsNullOrEmpty(e.Text) ? "X" : e.Text) : e.Text;
        var rect = new XRect(e.X, e.Y, e.Width <= 0 ? 1000 : e.Width, e.Height <= 0 ? e.FontSize * 1.4 : e.Height);
        var fmt = e.Align switch
        {
            "center" => XStringFormats.Center,
            "right"  => XStringFormats.CenterRight,
            _        => XStringFormats.CenterLeft,
        };
        gfx.DrawString(text, font, brush, rect, fmt);
    }

    public static async System.Threading.Tasks.Task ExportRasterized(string srcPdf, List<FillElement> elements, int dpi, string outPath)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(srcPdf);
        var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        int pages = (int)pdf.PageCount;

        string tempDir = Path.Combine(Path.GetTempPath(), "fillsign_raster_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var imgs = new List<string>();
        try
        {
            for (int i = 0; i < pages; i++)
            {
                using var bmp = await FillSignRender.RenderPageToBitmapAsync(srcPdf, i, dpi);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    foreach (var e in elements)
                    {
                        if (e.Page != i) continue;
                        var (px, py, pw, ph) = FillSignGeometry.PointRectToPixelRect(e.X, e.Y, e.Width, e.Height, dpi);
                        var gState = g.Save();
                        if (e.Rotation != 0)
                        {
                            float cx = (float)(px + pw / 2), cy = (float)(py + ph / 2);
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform((float)e.Rotation);
                            g.TranslateTransform(-cx, -cy);
                        }
                        if (e.Type is FillElementType.Signature or FillElementType.Stamp or FillElementType.Blur)
                        {
                            if (e.ImagePath != null && File.Exists(e.ImagePath))
                                using (var im = System.Drawing.Image.FromFile(e.ImagePath))
                                    g.DrawImage(im, (float)px, (float)py, (float)pw, (float)ph);
                        }
                        else if (e.Type == FillElementType.Redact)
                        {
                            var rc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(e.ColorHex);
                            byte a = e.Opacity < 100 ? (byte)(255 * e.Opacity / 100.0) : rc.A;
                            using var rb = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(a, rc.R, rc.G, rc.B));
                            g.FillRectangle(rb, (float)px, (float)py, (float)pw, (float)ph);
                        }
                        else if (e.Type == FillElementType.Draw)
                        {
                            DrawShapeRaster(g, e, dpi);
                        }
                        else
                        {
                            var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(e.ColorHex);
                            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(col.A, col.R, col.G, col.B));
                            var style = (e.Bold ? System.Drawing.FontStyle.Bold : 0) | (e.Italic ? System.Drawing.FontStyle.Italic : 0)
                                      | (e.Underline ? System.Drawing.FontStyle.Underline : 0);
                            float emPx = (float)FillSignGeometry.PointsToPixels(e.FontSize, dpi);
                            using var font = new System.Drawing.Font(e.FontFamily, emPx, style, System.Drawing.GraphicsUnit.Pixel);
                            string text = e.Type == FillElementType.Check && string.IsNullOrEmpty(e.Text) ? "X" : e.Text;
                            using var sf = new System.Drawing.StringFormat
                            {
                                Alignment = e.Align switch
                                {
                                    "center" => System.Drawing.StringAlignment.Center,
                                    "right"  => System.Drawing.StringAlignment.Far,
                                    _        => System.Drawing.StringAlignment.Near,
                                },
                                LineAlignment = System.Drawing.StringAlignment.Center
                            };
                            float boxW = (float)(pw <= 0 ? 1000 : pw);
                            float boxH = (float)(ph <= 0 ? emPx * 1.4 : ph);
                            g.DrawString(text, font, brush, new System.Drawing.RectangleF((float)px, (float)py, boxW, boxH), sf);
                        }
                        g.Restore(gState);
                    }
                }
                string f = Path.Combine(tempDir, $"page_{i:D3}.png");
                bmp.Save(f, System.Drawing.Imaging.ImageFormat.Png);
                imgs.Add(f);
            }
            await FileToolsService.ImagesToPdfAsync(imgs.ToArray(), outPath);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }

    static void DrawShapeRaster(System.Drawing.Graphics g, FillElement e, int dpi)
    {
        var mc = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(e.ColorHex);
        bool highlight = e.Shape == "highlight";
        byte a = highlight ? (byte)(255 * (e.Opacity < 100 ? e.Opacity : 40) / 100.0)
                           : (e.Opacity < 100 ? (byte)(255 * e.Opacity / 100.0) : mc.A);
        float widthPx = (float)System.Math.Max(1, FillSignGeometry.PointsToPixels(e.StrokeWidth <= 0 ? 2 : e.StrokeWidth, dpi));
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(a, mc.R, mc.G, mc.B), widthPx)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round
        };
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float X = (float)FillSignGeometry.PointsToPixels(e.X, dpi);
        float Y = (float)FillSignGeometry.PointsToPixels(e.Y, dpi);
        float W = (float)FillSignGeometry.PointsToPixels(e.Width, dpi);
        float H = (float)FillSignGeometry.PointsToPixels(e.Height, dpi);
        System.Drawing.PointF P(int i) => new(
            X + (float)FillSignGeometry.PointsToPixels(e.Points[i].X, dpi),
            Y + (float)FillSignGeometry.PointsToPixels(e.Points[i].Y, dpi));

        switch (e.Shape)
        {
            case "rect": g.DrawRectangle(pen, X, Y, W, H); break;
            case "ellipse": g.DrawEllipse(pen, X, Y, W, H); break;
            case "line": if (e.Points.Count >= 2) g.DrawLine(pen, P(0), P(1)); break;
            case "arrow":
                if (e.Points.Count >= 2)
                {
                    var s = P(0); var t = P(1);
                    g.DrawLine(pen, s, t);
                    foreach (var (hx, hy) in ArrowHead(s.X, s.Y, t.X, t.Y, widthPx))
                        g.DrawLine(pen, t.X, t.Y, (float)hx, (float)hy);
                }
                break;
            default:
                if (e.Points.Count >= 2)
                {
                    var pts = new System.Drawing.PointF[e.Points.Count];
                    for (int i = 0; i < e.Points.Count; i++) pts[i] = P(i);
                    g.DrawLines(pen, pts);
                }
                break;
        }
    }

    /// <summary>Public entry — vector path with automatic rasterized fallback.</summary>
    public static void Export(string srcPdf, List<FillElement> elements, string outPath)
    {
        EnsureResolver();
        try { ExportVector(srcPdf, elements, outPath); }
        catch (System.Exception)
        {
            ExportRasterized(srcPdf, elements, 150, outPath).GetAwaiter().GetResult();
        }
    }
}

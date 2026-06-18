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
        }
        if (st != null) gfx.Restore(st);
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

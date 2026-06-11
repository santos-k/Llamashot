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
                if (e.ImagePath != null && File.Exists(e.ImagePath))
                    gfx.DrawImage(XImage.FromFile(e.ImagePath), e.X, e.Y, e.Width, e.Height);
                break;
        }
    }

    static void DrawText(XGraphics gfx, FillElement e, bool glyph)
    {
        var style = (e.Bold ? XFontStyleEx.Bold : 0) | (e.Italic ? XFontStyleEx.Italic : 0);
        var font = new XFont(e.FontFamily, e.FontSize, style);
        var brush = new XSolidBrush(Color(e.ColorHex));
        string text = glyph ? (string.IsNullOrEmpty(e.Text) ? "X" : e.Text) : e.Text;
        var rect = new XRect(e.X, e.Y, e.Width <= 0 ? 1000 : e.Width, e.Height <= 0 ? e.FontSize * 1.4 : e.Height);
        gfx.DrawString(text, font, brush, rect, XStringFormats.CenterLeft);
    }

    /// <summary>Public entry — vector path with automatic rasterized fallback (fallback added in Task 9).</summary>
    public static void Export(string srcPdf, List<FillElement> elements, string outPath)
    {
        EnsureResolver();
        ExportVector(srcPdf, elements, outPath);
    }
}

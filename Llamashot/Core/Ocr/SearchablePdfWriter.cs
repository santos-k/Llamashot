using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Llamashot.Core.Ocr;

/// <summary>
/// Writes a "searchable PDF": each page renders the original image with an invisible text layer
/// (PDF render mode 3) positioned over the recognized words, so the document looks identical but
/// its text is selectable and searchable. Uses the base-14 Helvetica font — no embedding required.
/// Text layer is WinAnsi (Latin) in this iteration; non-Latin glyphs degrade to '?'.
///
/// The embedded page image is downscaled to a sensible print resolution (~200 DPI, long edge
/// capped) and JPEG-compressed so output stays small even when the source is a huge scan.
/// </summary>
public static class SearchablePdfWriter
{
    // Page image is normalized to ~200 DPI with the long edge capped — keeps file size sane while
    // staying crisp for on-screen reading and printing. The OCR itself ran at full resolution.
    private const int MaxEdgePx = 2200;
    private const double AssumedDpi = 200.0;
    private const double PtPerPx = 72.0 / AssumedDpi;   // image px (after downscale) → PDF points
    private const int JpegQuality = 80;

    /// <summary>One page: the page image plus its OCR result (word boxes in image pixels).</summary>
    public readonly record struct PageInput(BitmapSource Image, OcrPage Ocr);

    public static void Write(IReadOnlyList<PageInput> pages, string outputPath)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        var offsets = new List<long>();
        void WriteAscii(string s) { byte[] b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); }
        void Mark() => offsets.Add(fs.Position);

        int n = pages.Count;
        WriteAscii("%PDF-1.4\n");

        // 1: Catalog
        Mark(); WriteAscii("1 0 obj <</Type /Catalog /Pages 2 0 R>> endobj\n");

        // 2: Pages
        Mark();
        var kids = new StringBuilder("[");
        for (int i = 0; i < n; i++) { if (i > 0) kids.Append(' '); kids.Append($"{4 + i * 3} 0 R"); }
        kids.Append(']');
        WriteAscii($"2 0 obj <</Type /Pages /Kids {kids} /Count {n}>> endobj\n");

        // 3: Shared Helvetica font
        Mark();
        WriteAscii("3 0 obj <</Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding>> endobj\n");

        for (int i = 0; i < n; i++)
        {
            var ocr = pages[i].Ocr;
            int srcW = Math.Max(1, ocr.PixelWidth), srcH = Math.Max(1, ocr.PixelHeight);

            // Downscale the embedded image; coordinates map source px → points via factor k.
            double scale = Math.Min(1.0, (double)MaxEdgePx / Math.Max(srcW, srcH));
            double k = scale * PtPerPx;
            byte[] jpeg = EncodeJpeg(pages[i].Image, scale);

            double pageW = srcW * k, pageH = srcH * k;
            int pageObj = 4 + i * 3, contentObj = pageObj + 1, imgObj = pageObj + 2;

            // Page
            Mark();
            WriteAscii($"{pageObj} 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 {F(pageW)} {F(pageH)}] " +
                       $"/Contents {contentObj} 0 R /Resources <</XObject <</Img0 {imgObj} 0 R>> /Font <</F0 3 0 R>>>>>> endobj\n");

            // Content: draw image to fill page, then invisible text layer.
            byte[] content = BuildContent(ocr, pageW, pageH, k);
            Mark();
            WriteAscii($"{contentObj} 0 obj <</Length {content.Length}>>\nstream\n");
            fs.Write(content, 0, content.Length);
            WriteAscii("\nendstream endobj\n");

            // Image XObject (dimensions are the downscaled pixel size)
            int imgW = Math.Max(1, (int)Math.Round(srcW * scale)), imgH = Math.Max(1, (int)Math.Round(srcH * scale));
            Mark();
            WriteAscii($"{imgObj} 0 obj <</Type /XObject /Subtype /Image /Width {imgW} /Height {imgH} " +
                       $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length}>>\nstream\n");
            fs.Write(jpeg, 0, jpeg.Length);
            WriteAscii("\nendstream endobj\n");
        }

        long xref = fs.Position;
        int total = offsets.Count + 1; // + free object 0
        WriteAscii($"xref\n0 {total}\n0000000000 65535 f \n");
        foreach (long off in offsets) WriteAscii($"{off:D10} 00000 n \n");
        WriteAscii($"trailer <</Size {total} /Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n");
    }

    private static byte[] BuildContent(OcrPage ocr, double pageW, double pageH, double k)
    {
        var sb = new StringBuilder();
        // Image fills the whole page.
        sb.Append($"q {F(pageW)} 0 0 {F(pageH)} 0 0 cm /Img0 Do Q\n");

        foreach (var line in ocr.Lines)
        {
            foreach (var word in line.Words)
            {
                if (string.IsNullOrWhiteSpace(word.Text)) continue;
                var box = word.Box;
                if (box.Width <= 0 || box.Height <= 0) continue;

                string text = EscapeToLatin1(word.Text);
                if (text.Length == 0) continue;

                // Map the source-pixel box into PDF points (origin bottom-left).
                double bx = box.X * k, bw = box.Width * k, bh = box.Height * k;
                double fontSize = Math.Max(1.0, bh * 0.85);
                double baseline = pageH - box.Y * k - bh + bh * 0.18;

                // Stretch the (invisible) glyphs horizontally so the selectable run spans the word box.
                double glyphWidth = 0.5 * fontSize * Math.Max(1, word.Text.Length); // Helvetica ≈ 0.5em avg
                double tz = Math.Clamp(bw / glyphWidth * 100.0, 10, 1000);

                sb.Append("BT 3 Tr /F0 ").Append(F(fontSize)).Append(" Tf ")
                  .Append(F(tz)).Append(" Tz ")
                  .Append(F(bx)).Append(' ').Append(F(baseline)).Append(" Td (")
                  .Append(text).Append(") Tj ET\n");
            }
        }
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>Downscales by <paramref name="scale"/> and encodes baseline-RGB JPEG (flattening transparency onto white).</summary>
    private static byte[] EncodeJpeg(BitmapSource src, double scale)
    {
        BitmapSource s = src;
        if (scale < 0.999)
        {
            s = new TransformedBitmap(s, new ScaleTransform(scale, scale));
            s.Freeze();
        }

        if (s.Format == PixelFormats.Bgra32 || s.Format == PixelFormats.Pbgra32 || s.Format == PixelFormats.Bgr32)
        {
            int w = s.PixelWidth, h = s.PixelHeight;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawImage(s, new Rect(0, 0, w, h));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            s = new FormatConvertedBitmap(rtb, PixelFormats.Rgb24, null, 0);
        }
        else if (s.Format != PixelFormats.Bgr24 && s.Format != PixelFormats.Rgb24)
        {
            s = new FormatConvertedBitmap(s, PixelFormats.Rgb24, null, 0);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = JpegQuality };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(s));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Maps text to escaped Latin-1 for a PDF literal string; non-Latin chars become '?'.</summary>
    private static string EscapeToLatin1(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            char ch = c switch
            {
                '‘' or '’' => '\'',
                '“' or '”' => '"',
                '–' or '—' => '-',
                _ => c,
            };
            if (ch == '(' || ch == ')' || ch == '\\') { sb.Append('\\').Append(ch); }
            else if (ch >= ' ' && ch <= 'ÿ') sb.Append(ch);
            else if (ch > 'ÿ') sb.Append('?');
            // else: drop control chars
        }
        return sb.ToString();
    }
}

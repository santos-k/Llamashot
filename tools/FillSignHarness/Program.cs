using System;
using System.IO;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace FillSignHarness;

internal static class Program
{
    static readonly StringBuilder Sb = new();
    static int Pass, Fail;
    static string Dir = Path.Combine(Path.GetTempPath(), "fillsign_test");

    static void Check(string name, bool ok, string detail)
    {
        if (ok) Pass++; else Fail++;
        Sb.AppendLine($"{(ok ? "PASS" : "FAIL")} | {name} | {detail}");
    }

    [STAThread]
    static int Main()
    {
        Directory.CreateDirectory(Dir);

        try { Test_RenderPage(); }
        catch (Exception e) { Check("Render page 0 @150dpi", false, e.ToString()); }

        try { Test_PageSizePoints(); }
        catch (Exception e) { Check("Page size in points", false, e.ToString()); }

        try { Test_FillElementModel(); }
        catch (Exception e) { Check("FillElement holds values", false, e.ToString()); }

        try { Test_Geometry(); }
        catch (Exception e) { Check("Geometry px<->pt round-trip", false, e.ToString()); }

        try { Spike_PdfSharpVectorText(); }
        catch (Exception e) { Check("spike", false, e.ToString()); }

        try { Test_Segments(); }
        catch (Exception e) { Check("Detector finds H+V segments", false, e.ToString()); }

        try { Test_Regions(); }
        catch (Exception e) { Check("Detector classifies regions", false, e.ToString()); }

        try { Test_ExportText(); }
        catch (Exception e) { Check("Export writes selectable text", false, e.ToString()); }

        try { Test_ExportImage(); }
        catch (Exception e) { Check("Export embeds signature image", false, e.ToString()); }

        try { Test_Fallback(); }
        catch (Exception e) { Check("Rasterized fallback produces valid PDF", false, e.ToString()); }

        Sb.AppendLine($"\n=== {Pass} passed, {Fail} failed ===");
        File.WriteAllText(Path.Combine(Dir, "results.txt"), Sb.ToString());
        return Fail == 0 ? 0 : 1;
    }

    // Task 2: Page render utility
    static void Test_RenderPage()
    {
        string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf"));
        var bmp = Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(src, 0, 150).GetAwaiter().GetResult();
        Check("Render page 0 @150dpi", bmp != null && bmp.Width > 1000 && bmp.Height > 1000, $"{bmp?.Width}x{bmp?.Height}");
    }

    static void Test_PageSizePoints()
    {
        string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf"));
        var (wPt, hPt, rotation) = Llamashot.Core.FillSignRender.GetPageSizeAsync(src, 0).GetAwaiter().GetResult();
        Check("Page size in points", wPt > 200 && wPt < 1000, $"w={wPt} h={hPt}");
    }

    // Task 3: FillElement model
    static void Test_FillElementModel()
    {
        var el = new Llamashot.Models.FillElement { Page = 0, Type = Llamashot.Models.FillElementType.Text, X = 100, Y = 200, Width = 150, Height = 18, Text = "Santosh", FontFamily = "Arial", FontSize = 12, ColorHex = "#000000" };
        Check("FillElement holds values", el.Type == Llamashot.Models.FillElementType.Text && el.Text == "Santosh", $"x={el.X}");
    }

    // Task 4: Geometry
    static void Test_Geometry()
    {
        double px = Llamashot.Core.FillSignGeometry.PointsToPixels(72, 150);
        double pt = Llamashot.Core.FillSignGeometry.PixelsToPoints(150, 150);
        bool ok = System.Math.Abs(px - 150) < 0.01 && System.Math.Abs(pt - 72) < 0.01;
        var (rx, ry, rw, rh) = Llamashot.Core.FillSignGeometry.PixelRectToPointRect(208.33, 416.66, 100, 50, 150);
        ok &= System.Math.Abs(rx - 100) < 0.1 && System.Math.Abs(ry - 200) < 0.1;
        Check("Geometry px<->pt round-trip", ok, $"px={px:F2} pt={pt:F2} rx={rx:F2} ry={ry:F2}");
    }

    static void Spike_PdfSharpVectorText()
    {
        string src = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf");
        src = Path.GetFullPath(src);
        string outPath = Path.Combine(Dir, "spike_filled.pdf");

        using (var doc = PdfReader.Open(src, PdfDocumentOpenMode.Modify))
        {
            doc.Options.CompressContentStreams = false;
            var page = doc.Pages[0];
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            gfx.DrawString("LLAMASHOT-SPIKE-TOKEN", font, XBrushes.Black, new XPoint(80, 120));
            doc.Save(outPath);
        }

        Check("PDFsharp open+draw+save", File.Exists(outPath) && new FileInfo(outPath).Length > 0,
            $"out={new FileInfo(outPath).Length}b");

        bool found = false;
        using (var doc = PdfReader.Open(outPath, PdfDocumentOpenMode.ReadOnly))
        {
            var bytes = File.ReadAllBytes(outPath);
            found = Encoding.ASCII.GetString(bytes).Contains("LLAMASHOT-SPIKE-TOKEN")
                 || ContainsTokenInStreams(doc);
        }
        Check("PDFsharp text is vector/selectable", found, "token present in content");
    }

    static bool ContainsTokenInStreams(PdfDocument doc) => false;

    // Task 7: Vector text export
    static void Test_ExportText()
    {
        string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf"));
        string outPath = System.IO.Path.Combine(Dir, "export_text.pdf");
        var els = new System.Collections.Generic.List<Llamashot.Models.FillElement>
        {
            new() { Page=1, Type=Llamashot.Models.FillElementType.Text, X=120, Y=110, Width=200, Height=16, Text="SANTOSH-FILL-XYZ", FontFamily="Arial", FontSize=11, ColorHex="#000000" }
        };
        Llamashot.Core.FillSignExporter.Export(src, els, outPath);
        var bytes = System.IO.File.ReadAllBytes(outPath);
        bool present = System.Text.Encoding.ASCII.GetString(bytes).Contains("SANTOSH-FILL-XYZ");
        Check("Export writes selectable text", System.IO.File.Exists(outPath) && present, $"out={bytes.Length}b present={present}");
    }

    // Task 8: Image embedding test
    static void Test_ExportImage()
    {
        string sig = System.IO.Path.Combine(Dir, "sig.png");
        using (var b = new System.Drawing.Bitmap(200, 80))
        {
            using var g = System.Drawing.Graphics.FromImage(b);
            g.Clear(System.Drawing.Color.Transparent);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.Blue, 3);
            g.DrawCurve(pen, new[] { new System.Drawing.Point(5, 60), new System.Drawing.Point(60, 10), new System.Drawing.Point(120, 70), new System.Drawing.Point(195, 20) });
            b.Save(sig, System.Drawing.Imaging.ImageFormat.Png);
        }
        string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf"));
        string outPath = System.IO.Path.Combine(Dir, "export_image.pdf");
        var els = new System.Collections.Generic.List<Llamashot.Models.FillElement>
        {
            new() { Page=0, Type=Llamashot.Models.FillElementType.Signature, X=90, Y=380, Width=120, Height=48, ImagePath=sig }
        };
        Llamashot.Core.FillSignExporter.Export(src, els, outPath);
        using var d = PdfSharp.Pdf.IO.PdfReader.Open(outPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Check("Export embeds signature image", System.IO.File.Exists(outPath) && d.Pages.Count >= 1, $"pages={d.Pages.Count} size={new System.IO.FileInfo(outPath).Length}b");
    }

    // Task 9: Rasterized fallback
    static void Test_Fallback()
    {
        string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf"));
        string outPath = System.IO.Path.Combine(Dir, "export_raster.pdf");
        var els = new System.Collections.Generic.List<Llamashot.Models.FillElement>
        {
            new() { Page=1, Type=Llamashot.Models.FillElementType.Text, X=120, Y=110, Width=200, Height=16, Text="RASTER-FILL", FontSize=12, ColorHex="#000000", FontFamily="Arial" }
        };
        Llamashot.Core.FillSignExporter.ExportRasterized(src, els, 150, outPath).GetAwaiter().GetResult();
        int pc = Llamashot.Core.FileToolsService.GetPdfPageCountAsync(outPath).GetAwaiter().GetResult();
        Check("Rasterized fallback produces valid PDF", System.IO.File.Exists(outPath) && pc >= 2, $"pages={pc}");
    }

    // Task 6: Region classification
    static void Test_Regions()
    {
        using var bmp = new System.Drawing.Bitmap(600, 400);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 2);
            g.DrawRectangle(pen, 40, 40, 20, 20);
            g.DrawLine(pen, 40, 150, 300, 150);
            g.DrawRectangle(pen, 40, 200, 250, 40);
            for (int i = 1; i < 5; i++) g.DrawLine(pen, 40 + i * 50, 200, 40 + i * 50, 240);
        }
        var regions = Llamashot.Core.FillSignDetector.DetectRegions(bmp);
        int boxes = regions.FindAll(r => r.Kind == Llamashot.Core.RegionKind.Checkbox).Count;
        int combs = regions.FindAll(r => r.Kind == Llamashot.Core.RegionKind.Comb).Count;
        int unders = regions.FindAll(r => r.Kind == Llamashot.Core.RegionKind.Underline).Count;
        Check("Detector classifies regions", boxes >= 1 && combs >= 1 && unders >= 1, $"box={boxes} comb={combs} under={unders} total={regions.Count}");

        string src = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "137.pdf"));
        using var page = Llamashot.Core.FillSignRender.RenderPageToBitmapAsync(src, 1, 150).GetAwaiter().GetResult();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var real = Llamashot.Core.FillSignDetector.DetectRegions(page);
        sw.Stop();
        Check("Detector finds regions on 137.pdf p2", real.Count >= 10, $"regions={real.Count} ms={sw.ElapsedMilliseconds}");
    }

    // Task 5: Line-segment extraction
    static void Test_Segments()
    {
        using var bmp = new System.Drawing.Bitmap(400, 300);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.White);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 2);
            g.DrawLine(pen, 50, 100, 350, 100);
            g.DrawRectangle(pen, 50, 150, 80, 40);
        }
        var seg = Llamashot.Core.FillSignDetector.ExtractSegments(bmp, 0.1);
        bool hasH = seg.Horizontal.Exists(s => s.Length > 250);
        bool hasV = seg.Vertical.Exists(s => s.Length > 30);
        Check("Detector finds H+V segments", hasH && hasV, $"H={seg.Horizontal.Count} V={seg.Vertical.Count}");
    }
}

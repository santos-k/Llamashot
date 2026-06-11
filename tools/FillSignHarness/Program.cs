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

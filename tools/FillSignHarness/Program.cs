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
        try { Spike_PdfSharpVectorText(); }
        catch (Exception e) { Check("spike", false, e.ToString()); }
        Sb.AppendLine($"\n=== {Pass} passed, {Fail} failed ===");
        File.WriteAllText(Path.Combine(Dir, "results.txt"), Sb.ToString());
        return Fail == 0 ? 0 : 1;
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
}

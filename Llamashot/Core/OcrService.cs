using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Llamashot.Core.Ocr;

namespace Llamashot.Core;

/// <summary>Options controlling an OCR run over an image or PDF file.</summary>
public sealed class OcrOptions
{
    public string? EngineId { get; init; }
    public string LangCode { get; init; } = "eng";
    public int Dpi { get; init; } = 300;
    public string? PdfPassword { get; init; }
    public bool MakeSearchablePdf { get; init; }
    public string? SearchablePdfPath { get; init; }
    /// <summary>Keep the rendered page images on the result so a searchable PDF can be written later without re-running OCR.</summary>
    public bool KeepPageImages { get; init; }
}

/// <summary>Result of an OCR run: per-page results and the combined plain text.</summary>
public sealed class OcrDocument
{
    public List<OcrPage> Pages { get; init; } = new();
    /// <summary>Rendered page images (only populated when <see cref="OcrOptions.KeepPageImages"/> is set).</summary>
    public List<BitmapSource> PageImages { get; init; } = new();
    public string EngineName { get; init; } = "";
    public string CombinedText => string.Join("\n\n", Pages.Select(p => p.Text));
}

/// <summary>
/// Orchestrates OCR of an image or PDF file: loads/renders pages, recognizes each with the chosen
/// engine, and (optionally) writes a searchable PDF. Runs off the UI thread; supports progress + cancel.
/// </summary>
public static class OcrService
{
    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif", ".webp" };

    public static bool IsSupportedInput(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".pdf" || ImageExtensions.Contains(ext);
    }

    public static bool IsPdf(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static async Task<OcrDocument> RecognizeAsync(
        string inputPath, OcrOptions opts, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var engine = OcrEngines.ById(opts.EngineId)
            ?? throw new InvalidOperationException("No OCR engine is available on this system.");

        // Materialize the page images (prepared so word boxes align with what we may embed).
        var pageImages = IsPdf(inputPath)
            ? await RenderPdfPagesAsync(inputPath, opts.Dpi, opts.PdfPassword, ct)
            : new List<BitmapSource> { OcrImaging.Prepare(LoadImage(inputPath)) };

        var doc = new OcrDocument { EngineName = engine.DisplayName };
        var searchablePages = new List<SearchablePdfWriter.PageInput>();
        bool wantPdf = opts.MakeSearchablePdf && !string.IsNullOrEmpty(opts.SearchablePdfPath);

        for (int i = 0; i < pageImages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var img = pageImages[i];

            OcrPage page;
            try
            {
                page = await engine.RecognizeAsync(img, opts.LangCode, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                page = new OcrPage
                {
                    PixelWidth = img.PixelWidth,
                    PixelHeight = img.PixelHeight,
                    Error = ex.Message,
                };
            }
            doc.Pages.Add(page);
            if (opts.KeepPageImages) doc.PageImages.Add(img);

            if (wantPdf)
                searchablePages.Add(new SearchablePdfWriter.PageInput(img, page));

            progress?.Report((i + 1) * 100 / pageImages.Count);
        }

        if (wantPdf && searchablePages.Count > 0)
            await Task.Run(() => SearchablePdfWriter.Write(searchablePages, opts.SearchablePdfPath!), ct);

        return doc;
    }

    /// <summary>
    /// Writes a searchable PDF from an already-recognized document (requires it was run with
    /// <see cref="OcrOptions.KeepPageImages"/>). Avoids re-running OCR.
    /// </summary>
    public static async Task WriteSearchablePdfAsync(OcrDocument doc, string outputPath, CancellationToken ct = default)
    {
        if (doc.PageImages.Count == 0)
            throw new InvalidOperationException("No page images retained — re-run OCR with KeepPageImages enabled.");

        var inputs = new List<SearchablePdfWriter.PageInput>();
        for (int i = 0; i < doc.Pages.Count && i < doc.PageImages.Count; i++)
            inputs.Add(new SearchablePdfWriter.PageInput(doc.PageImages[i], doc.Pages[i]));

        await Task.Run(() => SearchablePdfWriter.Write(inputs, outputPath), ct);
    }

    private static BitmapSource LoadImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static async Task<List<BitmapSource>> RenderPdfPagesAsync(string pdfPath, int dpi, string? password, CancellationToken ct)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = string.IsNullOrEmpty(password)
            ? await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file)
            : await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, password);

        var pages = new List<BitmapSource>();
        for (uint i = 0; i < pdfDoc.PageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var page = pdfDoc.GetPage(i);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var options = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)(page.Size.Width * dpi / 72)
            };
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream.AsStreamForRead();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            pages.Add(bmp);
        }
        return pages;
    }
}

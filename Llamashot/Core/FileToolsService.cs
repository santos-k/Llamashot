using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

public static class FileToolsService
{
    // =====================================================================
    //  Password-protected PDF support
    // =====================================================================

    /// <summary>Loads a PDF, supplying a password when one is provided.</summary>
    private static async Task<Windows.Data.Pdf.PdfDocument> LoadPdfAsync(Windows.Storage.IStorageFile file, string? password)
        => string.IsNullOrEmpty(password)
            ? await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file)
            : await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, password);

    /// <summary>True if the PDF requires a password to open.</summary>
    public static async Task<bool> IsPdfEncryptedAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            return false;
        }
        catch
        {
            // LoadFromFileAsync throws for password-protected (and unreadable) PDFs.
            return true;
        }
    }

    /// <summary>True if the given password successfully opens the PDF.</summary>
    public static async Task<bool> TryUnlockPdfAsync(string path, string password)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, password);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task ImagesToPdfAsync(string[] imagePaths, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() => ImagesToPdfCore(imagePaths, outputPath, progress));
    }

    // Synchronous PDF assembler. Safe to call from any thread (no UI affinity).
    private static void ImagesToPdfCore(string[] imagePaths, string outputPath, IProgress<int>? progress)
    {
        {
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            var offsets = new List<long>();
            int objNum = 0;

            int imageCount = imagePaths.Length;
            int objsPerImage = 3; // page + content stream + image XObject
            int totalObjs = 2 + imageCount * objsPerImage; // catalog + pages + per-image

            // Precompute image data
            var imageDataList = new List<(byte[] jpegBytes, int width, int height)>();
            for (int i = 0; i < imageCount; i++)
            {
                string path = imagePaths[i];
                byte[] jpegBytes;
                int width, height;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    width = frame.PixelWidth;
                    height = frame.PixelHeight;

                    bool isJpeg = string.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(Path.GetExtension(path), ".jpeg", StringComparison.OrdinalIgnoreCase);

                    if (isJpeg)
                    {
                        stream.Position = 0;
                        jpegBytes = new byte[stream.Length];
                        stream.ReadExactly(jpegBytes);
                    }
                    else
                    {
                        BitmapSource source = frame;
                        if (source.Format != PixelFormats.Bgr24 && source.Format != PixelFormats.Bgr32
                            && source.Format != PixelFormats.Bgra32 && source.Format != PixelFormats.Rgb24)
                        {
                            source = new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0);
                        }

                        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        using var ms = new MemoryStream();
                        encoder.Save(ms);
                        jpegBytes = ms.ToArray();
                    }
                }

                imageDataList.Add((jpegBytes, width, height));
            }

            void WriteAscii(string text)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
            }

            // Header
            WriteAscii("%PDF-1.4\n");

            // Object 1: Catalog
            offsets.Add(fs.Position);
            objNum++;
            WriteAscii($"{objNum} 0 obj <</Type /Catalog /Pages 2 0 R>> endobj\n");

            // Object 2: Pages (we'll write kids after we know page obj numbers)
            // Page objects start at obj 3, each image uses 3 objects: page, content, image
            // So page obj numbers: 3, 6, 9, ...
            offsets.Add(fs.Position);
            objNum++;
            var kidsBuilder = new StringBuilder();
            kidsBuilder.Append('[');
            for (int i = 0; i < imageCount; i++)
            {
                int pageObjNum = 3 + i * objsPerImage;
                if (i > 0) kidsBuilder.Append(' ');
                kidsBuilder.Append($"{pageObjNum} 0 R");
            }
            kidsBuilder.Append(']');
            WriteAscii($"{objNum} 0 obj <</Type /Pages /Kids {kidsBuilder} /Count {imageCount}>> endobj\n");

            // For each image: write page, content stream, image XObject
            for (int i = 0; i < imageCount; i++)
            {
                var (jpegBytes, imgWidth, imgHeight) = imageDataList[i];
                int pageObjNum = 3 + i * objsPerImage;
                int contentObjNum = pageObjNum + 1;
                int imgObjNum = pageObjNum + 2;

                // Page object
                offsets.Add(fs.Position);
                objNum++;
                WriteAscii($"{pageObjNum} 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 {imgWidth} {imgHeight}] " +
                    $"/Contents {contentObjNum} 0 R /Resources <</XObject <</Img0 {imgObjNum} 0 R>>>>>> endobj\n");

                // Content stream
                string contentStr = $"q {imgWidth} 0 0 {imgHeight} 0 0 cm /Img0 Do Q";
                byte[] contentBytes = Encoding.ASCII.GetBytes(contentStr);
                offsets.Add(fs.Position);
                objNum++;
                WriteAscii($"{contentObjNum} 0 obj <</Length {contentBytes.Length}>>\nstream\n");
                fs.Write(contentBytes, 0, contentBytes.Length);
                WriteAscii("\nendstream endobj\n");

                // Image XObject
                offsets.Add(fs.Position);
                objNum++;
                WriteAscii($"{imgObjNum} 0 obj <</Type /XObject /Subtype /Image /Width {imgWidth} /Height {imgHeight} " +
                    $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length}>>\nstream\n");
                fs.Write(jpegBytes, 0, jpegBytes.Length);
                WriteAscii("\nendstream endobj\n");

                progress?.Report((i + 1) * 100 / imageCount);
            }

            // xref table
            long xrefOffset = fs.Position;
            int totalObjCount = objNum + 1; // +1 for the free object 0
            WriteAscii($"xref\n0 {totalObjCount}\n");
            WriteAscii("0000000000 65535 f \n");
            foreach (long offset in offsets)
            {
                WriteAscii($"{offset:D10} 00000 n \n");
            }

            // Trailer
            WriteAscii($"trailer <</Size {totalObjCount} /Root 1 0 R>>\nstartxref\n{xrefOffset}\n%%EOF\n");
        }
    }

    // =====================================================================
    //  Page composer (Images → PDF with page size, margins, multiple images
    //  per page, free move / resize / 360° rotate). Each placement carries a
    //  pre-computed cm matrix that maps the image's unit square to page points.
    // =====================================================================

    /// <summary>One image placed on a page, with the affine that maps its unit square to PDF points.</summary>
    public sealed class ComposePlacement
    {
        public required string SourcePath { get; init; }
        /// <summary>PDF cm matrix [a b c d e f] mapping the image unit square → page points.</summary>
        public required double[] Cm { get; init; }
    }

    /// <summary>A single composed page: size in PDF points (1/72"), background, and its images (back→front).</summary>
    public sealed class ComposePageSpec
    {
        public double WidthPt { get; set; }
        public double HeightPt { get; set; }
        public (double r, double g, double b) Background { get; set; } = (1, 1, 1);
        public List<ComposePlacement> Images { get; } = new();
    }

    public static async Task ComposePdfAsync(IReadOnlyList<ComposePageSpec> pages, string outputPath, IProgress<int>? progress = null)
        => await Task.Run(() => ComposePdfCore(pages, outputPath, progress));

    private static void ComposePdfCore(IReadOnlyList<ComposePageSpec> pages, string outputPath, IProgress<int>? progress)
    {
        static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        // Encode every distinct source once; a placement reuses the shared XObject.
        var encoded = new Dictionary<string, (byte[] jpeg, int w, int h)>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
            foreach (var img in page.Images)
                if (!encoded.ContainsKey(img.SourcePath))
                    encoded[img.SourcePath] = EncodeJpeg(img.SourcePath);

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        var offsets = new List<long>();
        void WriteAscii(string text) { byte[] b = Encoding.ASCII.GetBytes(text); fs.Write(b, 0, b.Length); }

        // --- Object-number assignment (write order == number order) ---
        int num = 2; // 1 = catalog, 2 = pages
        var imgObjNum = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pageNums = new List<int>();
        var contentNums = new List<int>();
        foreach (var page in pages)
        {
            pageNums.Add(++num);
            contentNums.Add(++num);
        }
        // Image XObjects are written after all pages/contents.
        foreach (var kv in encoded) imgObjNum[kv.Key] = ++num;

        WriteAscii("%PDF-1.4\n");

        // 1: Catalog
        offsets.Add(fs.Position);
        WriteAscii("1 0 obj <</Type /Catalog /Pages 2 0 R>> endobj\n");

        // 2: Pages
        offsets.Add(fs.Position);
        var kids = new StringBuilder("[");
        for (int i = 0; i < pageNums.Count; i++) { if (i > 0) kids.Append(' '); kids.Append($"{pageNums[i]} 0 R"); }
        kids.Append(']');
        WriteAscii($"2 0 obj <</Type /Pages /Kids {kids} /Count {pages.Count}>> endobj\n");

        // Page objects + their content streams
        for (int p = 0; p < pages.Count; p++)
        {
            var page = pages[p];
            int pageObj = pageNums[p];
            int contentObj = contentNums[p];

            // Resources: list this page's images as /Img0, /Img1, ...
            var xres = new StringBuilder();
            for (int i = 0; i < page.Images.Count; i++)
                xres.Append($"/Img{i} {imgObjNum[page.Images[i].SourcePath]} 0 R ");

            offsets.Add(fs.Position);
            WriteAscii($"{pageObj} 0 obj <</Type /Page /Parent 2 0 R /MediaBox [0 0 {F(page.WidthPt)} {F(page.HeightPt)}] " +
                       $"/Contents {contentObj} 0 R /Resources <</XObject <<{xres.ToString().TrimEnd()}>>>>>> endobj\n");

            // Content: background fill, then each image with its cm matrix.
            var sb = new StringBuilder();
            sb.Append($"{F(page.Background.r)} {F(page.Background.g)} {F(page.Background.b)} rg 0 0 {F(page.WidthPt)} {F(page.HeightPt)} re f\n");
            for (int i = 0; i < page.Images.Count; i++)
            {
                var m = page.Images[i].Cm;
                sb.Append($"q {F(m[0])} {F(m[1])} {F(m[2])} {F(m[3])} {F(m[4])} {F(m[5])} cm /Img{i} Do Q\n");
            }
            byte[] content = Encoding.ASCII.GetBytes(sb.ToString());
            offsets.Add(fs.Position);
            WriteAscii($"{contentObj} 0 obj <</Length {content.Length}>>\nstream\n");
            fs.Write(content, 0, content.Length);
            WriteAscii("\nendstream endobj\n");

            progress?.Report((p + 1) * 90 / Math.Max(1, pages.Count));
        }

        // Image XObjects
        foreach (var kv in encoded)
        {
            var (jpeg, w, h) = kv.Value;
            offsets.Add(fs.Position);
            WriteAscii($"{imgObjNum[kv.Key]} 0 obj <</Type /XObject /Subtype /Image /Width {w} /Height {h} " +
                       $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length}>>\nstream\n");
            fs.Write(jpeg, 0, jpeg.Length);
            WriteAscii("\nendstream endobj\n");
        }

        // xref + trailer
        long xrefOffset = fs.Position;
        int totalObjCount = num + 1; // +1 for free object 0
        WriteAscii($"xref\n0 {totalObjCount}\n0000000000 65535 f \n");
        foreach (long off in offsets) WriteAscii($"{off:D10} 00000 n \n");
        WriteAscii($"trailer <</Size {totalObjCount} /Root 1 0 R>>\nstartxref\n{xrefOffset}\n%%EOF\n");
        progress?.Report(100);
    }

    /// <summary>Decodes any supported image to baseline-JPEG bytes (+ pixel size) for embedding in a PDF.</summary>
    private static (byte[] jpeg, int w, int h) EncodeJpeg(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;

        string ext = Path.GetExtension(path);
        bool isJpeg = string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase);
        if (isJpeg)
        {
            stream.Position = 0;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            return (bytes, width, height);
        }

        BitmapSource src = frame;
        // Flatten onto white so transparent PNGs (logos, signatures) don't turn black.
        if (src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32 || src.Format == PixelFormats.Bgr32)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawImage(src, new Rect(0, 0, width, height));
            }
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            src = new FormatConvertedBitmap(rtb, PixelFormats.Rgb24, null, 0);
        }
        else if (src.Format != PixelFormats.Bgr24 && src.Format != PixelFormats.Rgb24)
        {
            src = new FormatConvertedBitmap(src, PixelFormats.Rgb24, null, 0);
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return (ms.ToArray(), width, height);
    }

    public static async Task<string[]> PdfToImagesAsync(string pdfPath, string outputDir, string format = "png", int dpi = 150, IProgress<int>? progress = null, string? password = null)
    {
        Directory.CreateDirectory(outputDir);

        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await LoadPdfAsync(file, password);
        uint pageCount = pdfDoc.PageCount;
        var outputPaths = new string[pageCount];

        for (uint i = 0; i < pageCount; i++)
        {
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

            string outputFile = Path.Combine(outputDir, $"page_{i + 1:D3}.{format}");
            SaveBitmapSource(bmp, outputFile);
            outputPaths[i] = outputFile;

            progress?.Report((int)((i + 1) * 100 / pageCount));
        }

        return outputPaths;
    }

    public static async Task<long> CompressImageAsync(string inputPath, string outputPath, int quality = 80, int maxDimension = 0)
    {
        await Task.Run(() =>
        {
            var source = LoadBitmapSourceFromFile(inputPath);
            int originalWidth = source.PixelWidth, originalHeight = source.PixelHeight;

            if (maxDimension > 0 && (source.PixelWidth > maxDimension || source.PixelHeight > maxDimension))
            {
                double scale = Math.Min((double)maxDimension / source.PixelWidth, (double)maxDimension / source.PixelHeight);
                source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                source.Freeze();
            }

            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            BitmapEncoder encoder;
            if (ext is ".png")
            {
                encoder = new PngBitmapEncoder();
            }
            else
            {
                encoder = new JpegBitmapEncoder { QualityLevel = quality };
            }

            encoder.Frames.Add(BitmapFrame.Create(source));
            using var ms = new MemoryStream();
            encoder.Save(ms);

            // Never produce a file larger than the source: if re-encoding bloated it
            // (already-optimized JPEG, low-entropy PNG, etc.) keep the original bytes.
            bool resized = maxDimension > 0 && (source.PixelWidth != originalWidth || source.PixelHeight != originalHeight);
            long originalLen = new FileInfo(inputPath).Length;
            if (!resized && ms.Length >= originalLen && !PathsEqual(inputPath, outputPath))
            {
                File.Copy(inputPath, outputPath, overwrite: true);
            }
            else
            {
                using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                ms.Position = 0;
                ms.CopyTo(fs);
            }
        });

        return new FileInfo(outputPath).Length;
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static async Task ResizeImageAsync(string inputPath, string outputPath, int width, int height, bool maintainAspect = true)
    {
        await Task.Run(() =>
        {
            var source = LoadBitmapSourceFromFile(inputPath);

            double scaleX = (double)width / source.PixelWidth;
            double scaleY = (double)height / source.PixelHeight;

            if (maintainAspect)
            {
                double uniformScale = Math.Min(scaleX, scaleY);
                scaleX = uniformScale;
                scaleY = uniformScale;
            }

            var transformed = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
            transformed.Freeze();

            SaveBitmapSource(transformed, outputPath);
        });
    }

    public static async Task ConvertImageFormatAsync(string inputPath, string outputPath, int jpegQuality = 90)
    {
        await Task.Run(() =>
        {
            var source = LoadBitmapSourceFromFile(inputPath);
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            var encoder = GetBitmapEncoder(ext);

            if (encoder is JpegBitmapEncoder jpeg)
                jpeg.QualityLevel = jpegQuality;

            encoder.Frames.Add(BitmapFrame.Create(source));
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        });
    }

    public static async Task CropImageAsync(string inputPath, string outputPath, Int32Rect cropRect)
    {
        await Task.Run(() =>
        {
            var source = LoadBitmapSourceFromFile(inputPath);
            var cropped = new CroppedBitmap(source, cropRect);
            cropped.Freeze();
            SaveBitmapSource(cropped, outputPath);
        });
    }

    public static async Task RotateImageAsync(string inputPath, string outputPath, int degrees)
    {
        await Task.Run(() =>
        {
            var source = LoadBitmapSourceFromFile(inputPath);
            var rotated = new TransformedBitmap(source, new RotateTransform(degrees));
            rotated.Freeze();
            SaveBitmapSource(rotated, outputPath);
        });
    }

    public static async Task FlipImageAsync(string inputPath, string outputPath, bool horizontal)
    {
        await Task.Run(() =>
        {
            var source = LoadBitmapSourceFromFile(inputPath);
            var transform = horizontal ? new ScaleTransform(-1, 1) : new ScaleTransform(1, -1);
            var flipped = new TransformedBitmap(source, transform);
            flipped.Freeze();
            SaveBitmapSource(flipped, outputPath);
        });
    }

    public static (int width, int height, long fileSize, string format) GetImageInfo(string path)
    {
        long fileSize = new FileInfo(path).Length;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        string format = decoder.CodecInfo?.FriendlyName ?? Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return (frame.PixelWidth, frame.PixelHeight, fileSize, format);
    }

    public static BitmapEncoder GetBitmapEncoder(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".png" => new PngBitmapEncoder(),
            ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tiff" or ".tif" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
    }

    public static async Task<int> GetPdfPageCountAsync(string pdfPath, string? password = null)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await LoadPdfAsync(file, password);
        return (int)pdfDoc.PageCount;
    }

    public static async Task MergePdfsAsync(string[] pdfPaths, string outputPath, IProgress<int>? progress = null, IReadOnlyList<string?>? passwords = null)
    {
        string tempDir = CreateTempDir("merge");
        try
        {
            var allImages = new List<string>();
            int totalPages = 0;

            // First pass: count total pages
            var pageCounts = new int[pdfPaths.Length];
            for (int f = 0; f < pdfPaths.Length; f++)
            {
                string? password = passwords != null && f < passwords.Count ? passwords[f] : null;
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPaths[f]);
                var pdfDoc = await LoadPdfAsync(file, password);
                pageCounts[f] = (int)pdfDoc.PageCount;
                totalPages += pageCounts[f];
            }

            int pagesProcessed = 0;

            // Second pass: render all pages
            for (int f = 0; f < pdfPaths.Length; f++)
            {
                string? password = passwords != null && f < passwords.Count ? passwords[f] : null;
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPaths[f]);
                var pdfDoc = await LoadPdfAsync(file, password);

                for (uint i = 0; i < pdfDoc.PageCount; i++)
                {
                    using var page = pdfDoc.GetPage(i);
                    var bmp = await RenderPdfPageAsync(page);

                    string tempFile = Path.Combine(tempDir, $"page_{pagesProcessed:D5}.jpg");
                    await Task.Run(() =>
                    {
                        var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                        encoder.Frames.Add(BitmapFrame.Create(bmp));
                        using var fs = new FileStream(tempFile, FileMode.Create);
                        encoder.Save(fs);
                    });
                    allImages.Add(tempFile);

                    pagesProcessed++;
                    progress?.Report(pagesProcessed * 50 / totalPages);
                }
            }

            var imgProgress = new Progress<int>(v => progress?.Report(50 + v / 2));
            await ImagesToPdfAsync(allImages.ToArray(), outputPath, imgProgress);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static async Task<string[]> SplitPdfAsync(string pdfPath, string outputDir, int fromPage, int toPage, IProgress<int>? progress = null, string? password = null)
    {
        Directory.CreateDirectory(outputDir);
        string tempDir = CreateTempDir("split");

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await LoadPdfAsync(file, password);

            int totalPages = toPage - fromPage + 1;
            var outputPaths = new string[totalPages];

            for (int p = fromPage; p <= toPage; p++)
            {
                uint pageIndex = (uint)(p - 1);
                using var page = pdfDoc.GetPage(pageIndex);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{p:D3}.jpg");
                await Task.Run(() =>
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = new FileStream(tempFile, FileMode.Create);
                    encoder.Save(fs);
                });

                string outputFile = Path.Combine(outputDir, $"page_{p:D3}.pdf");
                await ImagesToPdfAsync(new[] { tempFile }, outputFile);
                outputPaths[p - fromPage] = outputFile;

                progress?.Report((p - fromPage + 1) * 100 / totalPages);
            }

            return outputPaths;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static async Task RotatePdfAsync(string pdfPath, string outputPath, int degrees, IProgress<int>? progress = null, string? password = null)
    {
        string tempDir = CreateTempDir("rotate");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await LoadPdfAsync(file, password);
            uint pageCount = pdfDoc.PageCount;

            var tempImages = new List<string>();

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{i:D3}.jpg");

                var thread = new Thread(() =>
                {
                    var rotated = new TransformedBitmap(bmp, new RotateTransform(degrees));
                    rotated.Freeze();

                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(rotated));
                    using var fs = new FileStream(tempFile, FileMode.Create);
                    encoder.Save(fs);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                tempImages.Add(tempFile);
                progress?.Report((int)((i + 1) * 50 / pageCount));
            }

            var imgProgress = new Progress<int>(v => progress?.Report(50 + v / 2));
            await ImagesToPdfAsync(tempImages.ToArray(), outputPath, imgProgress);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static async Task WatermarkPdfAsync(string pdfPath, string outputPath, string watermarkText, double opacity = 0.3, int fontSize = 48, IProgress<int>? progress = null, string? password = null,
        string fontFamily = "Segoe UI", string colorHex = "#808080", string orientation = "diagonal-up", string position = "center")
    {
        string tempDir = CreateTempDir("watermark");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await LoadPdfAsync(file, password);
            uint pageCount = pdfDoc.PageCount;

            var tempImages = new List<string>();

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{i:D3}.jpg");
                double capturedFontSize = fontSize * 150.0 / 72.0; // pt -> 150-dpi pixels
                double capturedOpacity = opacity;
                string capturedText = watermarkText;
                string capturedFont = fontFamily;
                var capturedBrush = new SolidColorBrush(ParseColor(colorHex, Color.FromRgb(0x80, 0x80, 0x80)));
                capturedBrush.Freeze();
                double capturedAngle = AngleFor(orientation);
                string capturedPos = position;

                var thread = new Thread(() =>
                {
                    var watermarked = RenderOverlay(bmp, (dc, w, h) =>
                    {
                        var formattedText = new FormattedText(
                            capturedText,
                            CultureInfo.InvariantCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new Typeface(capturedFont),
                            capturedFontSize,
                            capturedBrush,
                            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

                        var (textX, textY) = PlaceText(capturedPos, w, h, formattedText.Width, formattedText.Height, w * 0.05);
                        double cx = textX + formattedText.Width / 2;
                        double cy = textY + formattedText.Height / 2;

                        dc.PushOpacity(capturedOpacity);
                        dc.PushTransform(new RotateTransform(capturedAngle, cx, cy));
                        dc.DrawText(formattedText, new Point(textX, textY));
                        dc.Pop(); // RotateTransform
                        dc.Pop(); // Opacity
                    });

                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(watermarked));
                    using var fs = new FileStream(tempFile, FileMode.Create);
                    encoder.Save(fs);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                tempImages.Add(tempFile);
                progress?.Report((int)((i + 1) * 50 / pageCount));
            }

            var imgProgress = new Progress<int>(v => progress?.Report(50 + v / 2));
            await ImagesToPdfAsync(tempImages.ToArray(), outputPath, imgProgress);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static async Task AddPageNumbersAsync(string pdfPath, string outputPath, string position = "bottom-center", IProgress<int>? progress = null, string? password = null,
        string fontFamily = "Segoe UI", double fontSizePt = 11, string colorHex = "#404040")
    {
        string tempDir = CreateTempDir("pagenums");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await LoadPdfAsync(file, password);
            uint pageCount = pdfDoc.PageCount;

            var tempImages = new List<string>();

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{i:D3}.jpg");
                uint capturedI = i;
                uint capturedCount = pageCount;
                string capturedPosition = position;
                string capturedFont = fontFamily;
                double capturedSize = fontSizePt * 150.0 / 72.0; // pt -> 150-dpi pixels
                var capturedBrush = new SolidColorBrush(ParseColor(colorHex, Color.FromRgb(64, 64, 64)));
                capturedBrush.Freeze();

                var thread = new Thread(() =>
                {
                    string pageText = $"Page {capturedI + 1} of {capturedCount}";

                    var numbered = RenderOverlay(bmp, (dc, w, h) =>
                    {
                        var formattedText = new FormattedText(
                            pageText,
                            CultureInfo.InvariantCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new Typeface(capturedFont),
                            capturedSize,
                            capturedBrush,
                            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

                        var (textX, textY) = PlaceText(capturedPosition, w, h, formattedText.Width, formattedText.Height, w * 0.04);
                        dc.DrawText(formattedText, new Point(textX, textY));
                    });

                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(numbered));
                    using var fs = new FileStream(tempFile, FileMode.Create);
                    encoder.Save(fs);
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                tempImages.Add(tempFile);
                progress?.Report((int)((i + 1) * 50 / pageCount));
            }

            var imgProgress = new Progress<int>(v => progress?.Report(50 + v / 2));
            await ImagesToPdfAsync(tempImages.ToArray(), outputPath, imgProgress);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>Saves a password-protected (encrypted) copy of a PDF. currentPassword unlocks the source if it is already encrypted.</summary>
    public static async Task ProtectPdfAsync(string inputPath, string outputPath, string newPassword, string? currentPassword = null)
    {
        await Task.Run(() =>
        {
            var doc = string.IsNullOrEmpty(currentPassword)
                ? PdfSharp.Pdf.IO.PdfReader.Open(inputPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify)
                : PdfSharp.Pdf.IO.PdfReader.Open(inputPath, currentPassword, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify);
            using (doc)
            {
                // Setting a user password enables encryption (PdfSharp 6 default scheme).
                var sec = doc.SecuritySettings;
                sec.UserPassword = newPassword;
                sec.OwnerPassword = newPassword;
                doc.Save(outputPath);
            }
        });
    }

    /// <summary>Saves an unprotected copy of a PDF. currentPassword unlocks the source (required if it is encrypted).</summary>
    public static async Task RemovePdfPasswordAsync(string inputPath, string outputPath, string? currentPassword = null)
    {
        await Task.Run(() =>
        {
            // Import every page into a fresh document — the new file carries no encryption.
            var src = string.IsNullOrEmpty(currentPassword)
                ? PdfSharp.Pdf.IO.PdfReader.Open(inputPath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import)
                : PdfSharp.Pdf.IO.PdfReader.Open(inputPath, currentPassword, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            using (src)
            using (var outDoc = new PdfSharp.Pdf.PdfDocument())
            {
                for (int i = 0; i < src.PageCount; i++) outDoc.AddPage(src.Pages[i]);
                outDoc.Save(outputPath);
            }
        });
    }

    /// <summary>Common-password preset names mapped to their character sets, for the recovery tool.</summary>
    public static readonly (string key, string label, string chars)[] RecoveryCharsets =
    {
        ("digits",  "Digits (0–9)",       "0123456789"),
        ("lower",   "Lowercase (a–z)",    "abcdefghijklmnopqrstuvwxyz"),
        ("alnum",   "Letters + digits",   "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"),
    };

    // A short built-in dictionary of frequently used passwords, tried before brute force.
    private static readonly string[] CommonPasswords =
    {
        "", "password", "123456", "12345678", "1234", "12345", "123456789", "1234567890",
        "qwerty", "abc123", "111111", "000000", "1234567", "admin", "letmein", "welcome",
        "monkey", "dragon", "master", "login", "passw0rd", "password1", "p@ssw0rd",
        "iloveyou", "sunshine", "princess", "football", "654321", "121212", "qwerty123",
        "secret", "test", "test123", "pass", "pass123", "root", "user", "guest", "demo",
        "india", "1q2w3e4r", "zaq12wsx", "qazwsx", "aaaaaa", "888888", "555555", "222222",
        "abcd1234", "a1b2c3d4", "changeme", "default", "temp", "temp123", "company",
        "december", "january", "summer", "winter", "spring", "autumn",
    };

    /// <summary>
    /// Attempts to recover an unknown open-password by trying a built-in dictionary, then brute force
    /// over <paramref name="charset"/> up to <paramref name="maxLength"/>. Returns the password (which may
    /// be the empty string for owner-only protection) or null if not found / cancelled.
    /// </summary>
    public static async Task<string?> RecoverPdfPasswordAsync(
        string inputPath, string charset, int maxLength,
        IProgress<(long tried, long total, string current)>? progress,
        System.Threading.CancellationToken token)
    {
        byte[] bytes = await File.ReadAllBytesAsync(inputPath, token);

        bool Try(string pw)
        {
            try
            {
                using var ms = new MemoryStream(bytes, false);
                using var doc = PdfSharp.Pdf.IO.PdfReader.Open(ms, pw, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                return doc.PageCount >= 0; // opened without throwing → correct password
            }
            catch { return false; }
        }

        return await Task.Run(() =>
        {
            long tried = 0;

            // 1) dictionary of common passwords
            foreach (var pw in CommonPasswords)
            {
                token.ThrowIfCancellationRequested();
                tried++;
                if (Try(pw)) return pw;
                progress?.Report((tried, -1, pw.Length == 0 ? "(no password)" : pw));
            }

            // 2) brute force, shortest first
            long total = 0, pow = 1;
            for (int len = 1; len <= maxLength; len++) { pow *= charset.Length; total += pow; }

            var buf = new char[maxLength];
            for (int len = 1; len <= maxLength; len++)
            {
                var found = BruteForce(charset, len, buf, Try, ref tried, total, progress, token);
                if (found != null) return found;
            }
            return null;
        }, token);
    }

    private static string? BruteForce(string cs, int len, char[] buf, Func<string, bool> Try,
        ref long tried, long total, IProgress<(long, long, string)>? progress, System.Threading.CancellationToken token)
    {
        int n = cs.Length;
        var idx = new int[len];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            for (int i = 0; i < len; i++) buf[i] = cs[idx[i]];
            string cand = new string(buf, 0, len);
            tried++;
            if (Try(cand)) return cand;
            if ((tried & 0x3FF) == 0) progress?.Report((tried, total, cand));

            int p = len - 1;
            while (p >= 0) { if (++idx[p] < n) break; idx[p] = 0; p--; }
            if (p < 0) break; // exhausted this length
        }
        return null;
    }

    public static async Task ExtractPdfPagesAsync(string pdfPath, int[] pageNumbers, string outputPath, IProgress<int>? progress = null, string? password = null)
    {
        string tempDir = CreateTempDir("extract");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await LoadPdfAsync(file, password);

            int totalPages = pageNumbers.Length;
            var tempImages = new List<string>();

            for (int i = 0; i < totalPages; i++)
            {
                uint pageIndex = (uint)(pageNumbers[i] - 1);
                using var page = pdfDoc.GetPage(pageIndex);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{i:D5}.jpg");
                await Task.Run(() =>
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = new FileStream(tempFile, FileMode.Create);
                    encoder.Save(fs);
                });
                tempImages.Add(tempFile);

                progress?.Report((i + 1) * 50 / totalPages);
            }

            var imgProgress = new Progress<int>(v => progress?.Report(50 + v / 2));
            await ImagesToPdfAsync(tempImages.ToArray(), outputPath, imgProgress);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Inserts pages into a base PDF after <paramref name="afterPage"/>. Each insert item may be an
    /// image (one page) or a PDF (all its pages, expanded in order). insertPasswords unlocks any
    /// protected insert PDFs (parallel to insertItemPaths).
    /// </summary>
    public static async Task InsertPdfPagesAsync(string basePdfPath, string[] insertItemPaths, int afterPage, string outputPath, IProgress<int>? progress = null, string? password = null, IList<string?>? insertPasswords = null)
    {
        string tempDir = CreateTempDir("insert");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(basePdfPath);
            var pdfDoc = await LoadPdfAsync(file, password);
            uint pageCount = pdfDoc.PageCount;

            var tempImages = new List<string>();

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{i:D5}.jpg");
                await Task.Run(() =>
                {
                    var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = new FileStream(tempFile, FileMode.Create);
                    encoder.Save(fs);
                });
                tempImages.Add(tempFile);

                progress?.Report((int)((i + 1) * 40 / pageCount));
            }

            // Expand insert items: images pass through, PDFs are rasterized to one image per page.
            var insertExpanded = new List<string>();
            for (int k = 0; k < insertItemPaths.Length; k++)
            {
                string ip = insertItemPaths[k];
                if (ip.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    string? ipw = insertPasswords != null && k < insertPasswords.Count ? insertPasswords[k] : null;
                    var ifile = await Windows.Storage.StorageFile.GetFileFromPathAsync(ip);
                    var idoc = await LoadPdfAsync(ifile, ipw);
                    for (uint pi = 0; pi < idoc.PageCount; pi++)
                    {
                        using var ipage = idoc.GetPage(pi);
                        var ibmp = await RenderPdfPageAsync(ipage);
                        string itf = Path.Combine(tempDir, $"ins_{k:D3}_{pi:D5}.jpg");
                        await Task.Run(() =>
                        {
                            var encoder = new JpegBitmapEncoder { QualityLevel = 95 };
                            encoder.Frames.Add(BitmapFrame.Create(ibmp));
                            using var fs = new FileStream(itf, FileMode.Create);
                            encoder.Save(fs);
                        });
                        insertExpanded.Add(itf);
                    }
                }
                else insertExpanded.Add(ip);
            }

            // Build ordered list: base pages 1..afterPage, then expanded inserts, then remaining base pages
            var allImages = new List<string>();
            for (int i = 0; i < afterPage; i++)
                allImages.Add(tempImages[i]);

            allImages.AddRange(insertExpanded);

            for (int i = afterPage; i < tempImages.Count; i++)
                allImages.Add(tempImages[i]);

            var imgProgress = new Progress<int>(v => progress?.Report(40 + v * 60 / 100));
            await ImagesToPdfAsync(allImages.ToArray(), outputPath, imgProgress);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    public static bool IsFFmpegAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(3000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    public static async Task<(TimeSpan duration, int width, int height, string codec)> GetVideoInfoAsync(string path)
    {
        return await Task.Run(() =>
        {
            var psi = new ProcessStartInfo("ffprobe",
                $"-v error -select_streams v:0 -show_entries stream=width,height,codec_name -show_entries format=duration -of default=noprint_wrappers=1 \"{path}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(10000);

            // Output is order-independent "key=value" lines (ffprobe doesn't honor the
            // requested column order in csv mode), e.g. width=320 / height=240 / duration=4.0
            int width = 0, height = 0;
            string codec = "unknown";
            double durationSec = 0;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();
                switch (key)
                {
                    case "width": int.TryParse(val, out width); break;
                    case "height": int.TryParse(val, out height); break;
                    case "codec_name": codec = val; break;
                    case "duration":
                        double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out durationSec);
                        break;
                }
            }

            return (TimeSpan.FromSeconds(durationSec), width, height, codec);
        });
    }

    private static async Task RunFFmpegAsync(string arguments, TimeSpan? totalDuration = null, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            var psi = new ProcessStartInfo("ffmpeg", $"-y {arguments}")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;

            // Read stderr for progress (FFmpeg outputs progress to stderr)
            double totalMs = totalDuration?.TotalMilliseconds ?? 0;
            var stderr = new StringBuilder();

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                stderr.AppendLine(e.Data);
                if (totalMs > 0 && progress != null && e.Data.Contains("time="))
                {
                    // Parse "time=HH:MM:SS.ff"
                    var match = Regex.Match(e.Data, @"time=(\d+):(\d+):(\d+)\.(\d+)");
                    if (match.Success)
                    {
                        int h = int.Parse(match.Groups[1].Value);
                        int m = int.Parse(match.Groups[2].Value);
                        int sec = int.Parse(match.Groups[3].Value);
                        double currentMs = (h * 3600 + m * 60 + sec) * 1000;
                        int pct = Math.Min(99, (int)(currentMs * 100 / totalMs));
                        progress.Report(pct);
                    }
                }
            };
            proc.BeginErrorReadLine();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new Exception($"FFmpeg failed (exit code {proc.ExitCode})");

            progress?.Report(100);
        });
    }

    public static async Task TrimVideoAsync(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<int>? progress = null)
    {
        string startStr = start.ToString(@"hh\:mm\:ss\.ff");
        string endStr = end.ToString(@"hh\:mm\:ss\.ff");
        var duration = end - start;
        await RunFFmpegAsync($"-i \"{inputPath}\" -ss {startStr} -to {endStr} -c copy \"{outputPath}\"", duration, progress);
    }

    public static async Task TrimAudioAsync(string inputPath, string outputPath, TimeSpan start, TimeSpan end, IProgress<int>? progress = null)
    {
        string startStr = start.ToString(@"hh\:mm\:ss\.fff");
        string endStr = end.ToString(@"hh\:mm\:ss\.fff");
        string durStr = (end - start).ToString(@"hh\:mm\:ss\.fff");
        var duration = end - start;

        // Preserve the source's embedded cover art (MP3 ID3) when present. Output-side
        // seeking would discard the cover frame (it sits at t=0), so we seek the audio
        // input only (input-side -ss/-t) and re-attach the untouched cover image.
        string? cover = Path.GetExtension(outputPath).ToLowerInvariant() == ".mp3"
            ? await TryExtractCoverAsync(inputPath)
            : null;

        if (cover != null)
        {
            try
            {
                await RunFFmpegAsync(
                    $"-ss {startStr} -t {durStr} -i \"{inputPath}\" -i \"{cover}\" -map 0:a -map 1:0 " +
                    $"-c copy -id3v2_version 3 -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" " +
                    $"-disposition:v attached_pic \"{outputPath}\"",
                    duration, progress);
                return;
            }
            catch { /* fall back to a plain trim below */ }
            finally { try { File.Delete(cover); } catch { } }
        }

        await RunFFmpegAsync($"-i \"{inputPath}\" -ss {startStr} -to {endStr} -c copy \"{outputPath}\"", duration, progress);
    }

    // Pulls the embedded cover art (attached picture) out to a temp JPEG, or null if none.
    private static async Task<string?> TryExtractCoverAsync(string inputPath)
    {
        string cover = Path.Combine(Path.GetTempPath(), $"llamashot_cover_{Guid.NewGuid():N}.jpg");
        try
        {
            await RunFFmpegAsync($"-i \"{inputPath}\" -an -map 0:v:0 -frames:v 1 -q:v 2 \"{cover}\"", null, null);
        }
        catch { try { if (File.Exists(cover)) File.Delete(cover); } catch { } return null; }
        return (File.Exists(cover) && new FileInfo(cover).Length > 0) ? cover : null;
    }

    // Analyses the loudness envelope and returns, as fractions of the duration (0..1):
    //  - lowPeaks: the quiet dips (where the smoothed level falls well below the track's
    //    own peak) — one marker per dip,
    //  - startFraction/endFraction: where the strong audio begins/ends (for snapping the
    //    trim handles past the quiet intro/outro). Thresholds are relative to each track.
    public static async Task<(List<double> lowPeaks, double startFraction, double endFraction)>
        AnalyzeAudioLowPeaksAsync(string path)
    {
        var info = await GetVideoInfoAsync(path);
        double durSec = info.duration.TotalSeconds;
        var empty = (new List<double>(), 0.0, 1.0);
        if (durSec <= 0) return empty;

        // Decode mono PCM at 8 kHz — high enough that the anti-alias filter keeps the
        // musical energy (so loud passages stay loud), then measure loudness as windowed RMS.
        const int sr = 8000;
        byte[] pcm = await Task.Run(() =>
        {
            var psi = new ProcessStartInfo("ffmpeg", $"-v error -i \"{path}\" -ac 1 -ar {sr} -f s16le -")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            proc.ErrorDataReceived += (_, __) => { };
            proc.BeginErrorReadLine();                  // drain stderr so it can't deadlock
            using var ms = new MemoryStream();
            proc.StandardOutput.BaseStream.CopyTo(ms);
            proc.WaitForExit(120000);
            return ms.ToArray();
        });

        int samples = pcm.Length / 2;
        int wlen = (int)(sr * 0.05);                    // 50 ms RMS window
        int nw = samples / wlen;
        if (nw <= 2) return empty;

        var rms = new double[nw];
        for (int w = 0; w < nw; w++)
        {
            double sum = 0;
            int baseIdx = w * wlen * 2;
            for (int k = 0; k < wlen; k++)
            {
                short s = (short)(pcm[baseIdx + 2 * k] | (pcm[baseIdx + 2 * k + 1] << 8));
                double v = s / 32768.0;
                sum += v * v;
            }
            rms[w] = Math.Sqrt(sum / wlen);
        }

        // Light 3-window (150 ms) smoothing so a single quiet window isn't a false dip.
        var sm = new double[nw];
        for (int i = 0; i < nw; i++)
        {
            double a = i > 0 ? rms[i - 1] : rms[i];
            double c = i < nw - 1 ? rms[i + 1] : rms[i];
            sm[i] = (a + rms[i] + c) / 3.0;
        }

        double peak = sm.Max();
        if (peak <= 0) return empty;

        double strongThresh = peak * 0.30;  // "real audio" level
        double lowThresh = peak * 0.08;     // a genuine quiet pause (not an inter-beat dip)

        int firstStrong = Array.FindIndex(sm, v => v >= strongThresh);
        int lastStrong = Array.FindLastIndex(sm, v => v >= strongThresh);
        if (firstStrong < 0) { firstStrong = 0; lastStrong = nw - 1; }

        double ToFrac(int w) => Math.Clamp((w + 0.5) / nw, 0, 1);

        // Collect sustained quiet pauses only: a region must stay below the threshold for
        // at least ~0.7s to count (so phrase gaps and momentary dips are ignored). Each
        // region yields one marker at its lowest point.
        int minRegion = Math.Max(1, (int)(0.7 / 0.05));   // 0.7s
        var regions = new List<(int minIdx, int len)>();
        for (int i = 0; i < nw;)
        {
            if (sm[i] < lowThresh)
            {
                int j = i, minIdx = i;
                while (j < nw && sm[j] < lowThresh) { if (sm[j] < sm[minIdx]) minIdx = j; j++; }
                if (j - i >= minRegion) regions.Add((minIdx, j - i));
                i = j;
            }
            else i++;
        }

        // Keep only the most prominent pauses (longest), capped so the waveform stays
        // readable even on long compilations; then restore chronological order.
        const int maxMarkers = 12;
        var lowPeaks = regions
            .OrderByDescending(r => r.len)
            .Take(maxMarkers)
            .Select(r => ToFrac(r.minIdx))
            .OrderBy(f => f)
            .ToList();

        // Snap the handles just past the intro / before the outro (~50 ms margin so the
        // first/last beat isn't clipped), landing the cut in the adjacent quiet dip.
        double startFraction = ToFrac(Math.Max(0, firstStrong - 1));
        double endFraction = ToFrac(Math.Min(nw - 1, lastStrong + 1));
        if (endFraction <= startFraction) { startFraction = 0; endFraction = 1; }

        return (lowPeaks, startFraction, endFraction);
    }

    // Detects leading/trailing silence and returns the suggested keep-region [start, end].
    // Falls back to [0, duration] when no edge silence is found.
    public static async Task<(TimeSpan start, TimeSpan end)> DetectSilenceBoundsAsync(
        string path, double noiseDb = -30, double minSilenceSec = 0.4)
    {
        var info = await GetVideoInfoAsync(path);
        TimeSpan total = info.duration;

        string stderr = await Task.Run(() =>
        {
            var psi = new ProcessStartInfo("ffmpeg",
                $"-i \"{path}\" -af silencedetect=noise={noiseDb}dB:d={minSilenceSec.ToString(CultureInfo.InvariantCulture)} -f null -")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            return err;
        });

        // Collect (start,end) silence intervals from silencedetect's stderr log.
        var intervals = new List<(double s, double e)>();
        double? pending = null;
        foreach (Match m in Regex.Matches(stderr, @"silence_(start|end):\s*(-?\d+(?:\.\d+)?)"))
        {
            double v = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            if (m.Groups[1].Value == "start") pending = v;
            else { intervals.Add((pending ?? 0, v)); pending = null; }
        }
        if (pending.HasValue) intervals.Add((pending.Value, total.TotalSeconds)); // silence ran to EOF

        double startSec = 0, endSec = total.TotalSeconds;
        // Leading silence: an interval that begins at (or very near) the start.
        var lead = intervals.Where(iv => iv.s <= 0.3).ToList();
        if (lead.Count > 0) startSec = lead[0].e;
        // Trailing silence: an interval that ends at (or very near) the end.
        var trail = intervals.Where(iv => iv.e >= total.TotalSeconds - 0.3).ToList();
        if (trail.Count > 0) endSec = trail[^1].s;

        // Guard against nonsensical bounds.
        if (endSec <= startSec) { startSec = 0; endSec = total.TotalSeconds; }
        return (TimeSpan.FromSeconds(Math.Max(0, startSec)), TimeSpan.FromSeconds(Math.Min(total.TotalSeconds, endSec)));
    }

    public static async Task<string> GenerateWaveformAsync(string audioPath, int width = 1400, int height = 150)
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"llamashot_wf_{Guid.NewGuid():N}.png");
        await RunFFmpegAsync(
            $"-i \"{audioPath}\" -filter_complex \"showwavespic=s={width}x{height}:colors=#4CAF50|#4CAF50:scale=sqrt\" -frames:v 1 \"{outputPath}\"",
            null, null);
        return outputPath;
    }

    public static async Task<TimeSpan> GetAudioDurationAsync(string path)
    {
        var info = await GetVideoInfoAsync(path); // ffprobe works for audio too
        return info.duration;
    }

    // Grabs a single representative frame from a video as a small JPEG thumbnail.
    // Returns the temp file path, or null if extraction failed (caller shows a placeholder).
    public static async Task<string?> GenerateVideoThumbnailAsync(string videoPath, int width = 360)
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"llamashot_vthumb_{Guid.NewGuid():N}.jpg");
        try
        {
            // Seek ~1s in (fast pre-input seek) and grab one frame scaled to `width`.
            await RunFFmpegAsync(
                $"-ss 1 -i \"{videoPath}\" -frames:v 1 -vf \"scale={width}:-1\" -q:v 4 \"{outputPath}\"",
                null, null);
        }
        catch
        {
            // Very short clips can fail the 1s seek — retry from the first frame.
            try
            {
                await RunFFmpegAsync(
                    $"-i \"{videoPath}\" -frames:v 1 -vf \"scale={width}:-1\" -q:v 4 \"{outputPath}\"",
                    null, null);
            }
            catch { return null; }
        }
        return File.Exists(outputPath) ? outputPath : null;
    }

    public static bool IsAudioExtension(string ext) =>
        ext.ToLowerInvariant() is ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wma" or ".opus";

    public static async Task CropVideoAsync(string inputPath, string outputPath, int cropW, int cropH, int cropX, int cropY, IProgress<int>? progress = null)
    {
        var info = await GetVideoInfoAsync(inputPath);
        await RunFFmpegAsync($"-i \"{inputPath}\" -vf \"crop={cropW}:{cropH}:{cropX}:{cropY}\" -c:v libx264 -crf 18 -preset fast -c:a aac \"{outputPath}\"", info.duration, progress);
    }

    public static async Task RotateVideoAsync(string inputPath, string outputPath, int degrees, IProgress<int>? progress = null)
    {
        var info = await GetVideoInfoAsync(inputPath);
        string filter = degrees switch
        {
            90 => "transpose=1",
            180 => "transpose=1,transpose=1",
            270 => "transpose=2",
            _ => throw new ArgumentException($"Unsupported rotation: {degrees}")
        };
        await RunFFmpegAsync($"-i \"{inputPath}\" -vf \"{filter}\" -c:v libx264 -crf 18 -preset fast -c:a aac \"{outputPath}\"", info.duration, progress);
    }

    public static async Task FlipVideoAsync(string inputPath, string outputPath, bool horizontal, IProgress<int>? progress = null)
    {
        var info = await GetVideoInfoAsync(inputPath);
        string filter = horizontal ? "hflip" : "vflip";
        await RunFFmpegAsync($"-i \"{inputPath}\" -vf \"{filter}\" -c:v libx264 -crf 18 -preset fast -c:a aac \"{outputPath}\"", info.duration, progress);
    }

    public static async Task ExtractAudioAsync(string inputPath, string outputPath, IProgress<int>? progress = null)
    {
        var info = await GetVideoInfoAsync(inputPath);
        string ext = Path.GetExtension(outputPath).ToLowerInvariant();
        string codec = ext switch
        {
            ".mp3" => "-acodec libmp3lame -q:a 2",
            ".wav" => "",
            ".aac" => "-acodec aac",
            ".flac" => "-acodec flac",
            _ => "-acodec libmp3lame -q:a 2"
        };
        await RunFFmpegAsync($"-i \"{inputPath}\" -vn {codec} \"{outputPath}\"", info.duration, progress);
    }

    public static async Task ExportVideoAsync(string inputPath, string outputPath,
        TimeSpan? trimStart, TimeSpan? trimEnd,
        int rotateDegrees, bool flipH, bool flipV,
        int cropW, int cropH, int cropX, int cropY, int origW, int origH,
        IProgress<int>? progress = null)
    {
        var filters = new List<string>();

        if (cropW > 0 && cropH > 0 && (cropW != origW || cropH != origH || cropX != 0 || cropY != 0))
            filters.Add($"crop={cropW}:{cropH}:{cropX}:{cropY}");
        if (rotateDegrees == 90) filters.Add("transpose=1");
        else if (rotateDegrees == 180) { filters.Add("transpose=1"); filters.Add("transpose=1"); }
        else if (rotateDegrees == 270) filters.Add("transpose=2");
        if (flipH) filters.Add("hflip");
        if (flipV) filters.Add("vflip");

        string trimArgs = "";
        TimeSpan duration = TimeSpan.Zero;
        if (trimStart.HasValue && trimEnd.HasValue && trimEnd.Value > trimStart.Value)
        {
            trimArgs = $"-ss {trimStart.Value:hh\\:mm\\:ss\\.ff} -to {trimEnd.Value:hh\\:mm\\:ss\\.ff}";
            duration = trimEnd.Value - trimStart.Value;
        }
        else
        {
            var info = await GetVideoInfoAsync(inputPath);
            duration = info.duration;
        }

        string filterArg = filters.Count > 0 ? $"-vf \"{string.Join(",", filters)}\"" : "";
        string codecArgs = filters.Count > 0 ? "-c:v libx264 -crf 18 -preset fast -c:a aac" : "-c copy";

        await RunFFmpegAsync($"{trimArgs} -i \"{inputPath}\" {filterArg} {codecArgs} \"{outputPath}\"", duration, progress);
    }

    public static bool IsVideoExtension(string ext) =>
        ext.ToLowerInvariant() is ".mp4" or ".avi" or ".mov" or ".mkv" or ".wmv" or ".webm" or ".m4v" or ".flv" or ".3gp" or ".mpeg" or ".mpg";

    public static string FormatTimeSpan(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

    public static bool IsYtDlpAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo(FindYtDlpPath(), "--version")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string FindYtDlpPath()
    {
        // Check standard WinGet links directory
        string wingetLinks = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Links");
        string ytdlpPath = Path.Combine(wingetLinks, "yt-dlp.exe");
        if (File.Exists(ytdlpPath)) return ytdlpPath;
        return "yt-dlp"; // fallback to PATH
    }

    public static async Task<List<(string title, string duration, string url, string thumbnail, bool isPlaylist, string channel, string views, string age)>> FetchYouTubeVideosAsync(string inputUrl, string? playlistItems = null)
    {
        var results = new List<(string title, string duration, string url, string thumbnail, bool isPlaylist, string channel, string views, string age)>();
        string ytdlp = FindYtDlpPath();
        string sep = "|||";
        // title|||duration|||url|||thumbnail|||ie_key|||channel|||view_count|||upload_date
        string printFmt = $"%(title)s{sep}%(duration_string)s{sep}%(webpage_url)s{sep}%(thumbnail)s{sep}%(ie_key)s{sep}%(channel)s{sep}%(view_count)s{sep}%(upload_date)s";
        // Slice a search/playlist into a batch, e.g. "1-10" / "11-20".
        string itemsArg = string.IsNullOrEmpty(playlistItems) ? "" : $"--playlist-items {playlistItems} ";

        await Task.Run(() =>
        {
            // Use --flat-playlist with a unique separator (tabs are unreliable across process boundaries).
            // %(ie_key)s reports the extractor: "YoutubeTab" = a playlist entry, "Youtube" = a single video.
            var psi = new ProcessStartInfo(ytdlp,
                $"--flat-playlist {itemsArg}--print \"{printFmt}\" --no-warnings \"{inputUrl}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(60000);

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(sep);
                if (parts.Length >= 1)
                {
                    string title = parts[0].Trim();
                    string duration = parts.Length >= 2 ? parts[1].Trim() : "";
                    string url = parts.Length >= 3 ? parts[2].Trim() : "";
                    string thumb = parts.Length >= 4 ? parts[3].Trim() : "";
                    string ieKey = parts.Length >= 5 ? parts[4].Trim() : "";
                    string channel = parts.Length >= 6 ? parts[5].Trim() : "";
                    string viewCount = parts.Length >= 7 ? parts[6].Trim() : "";
                    string uploadDate = parts.Length >= 8 ? parts[7].Trim() : "";
                    if (title == "NA") title = "Untitled";
                    if (duration == "NA") duration = "";
                    if (url == "NA") url = "";
                    if (thumb == "NA") thumb = "";
                    if (channel == "NA") channel = "";
                    bool isPlaylist = ieKey.Equals("YoutubeTab", StringComparison.OrdinalIgnoreCase);
                    // For playlist entries without webpage_url, construct from video id
                    if (string.IsNullOrEmpty(url) && line.Contains("youtube.com"))
                        url = inputUrl;
                    if (!string.IsNullOrEmpty(title) && title != "Untitled")
                        results.Add((title, duration, !string.IsNullOrEmpty(url) ? url : inputUrl, thumb, isPlaylist,
                            channel, FormatViewCount(viewCount), FormatUploadAge(uploadDate)));
                }
            }

            // If no results from flat-playlist (single video), try direct
            if (results.Count == 0)
            {
                var psi2 = new ProcessStartInfo(ytdlp,
                    $"--print \"{printFmt}\" --no-download --no-warnings \"{inputUrl}\"")
                {
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var proc2 = Process.Start(psi2)!;
                string output2 = proc2.StandardOutput.ReadToEnd().Trim();
                proc2.WaitForExit(30000);

                if (!string.IsNullOrEmpty(output2))
                {
                    var parts = output2.Split(sep);
                    string title = parts.Length >= 1 ? parts[0].Trim() : "Unknown";
                    string duration = parts.Length >= 2 ? parts[1].Trim() : "";
                    string url = parts.Length >= 3 ? parts[2].Trim() : inputUrl;
                    string thumb = parts.Length >= 4 ? parts[3].Trim() : "";
                    string channel = parts.Length >= 6 ? parts[5].Trim() : "";
                    string viewCount = parts.Length >= 7 ? parts[6].Trim() : "";
                    string uploadDate = parts.Length >= 8 ? parts[7].Trim() : "";
                    if (channel == "NA") channel = "";
                    results.Add((title, duration, url, thumb, false,
                        channel, FormatViewCount(viewCount), FormatUploadAge(uploadDate)));
                }
            }
        });

        return results;
    }

    // "1234567" -> "1.2M views"; "" / "NA" -> "".
    private static string FormatViewCount(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "NA") return "";
        if (!long.TryParse(raw, out long n) || n < 0) return "";
        string num = n switch
        {
            >= 1_000_000_000 => $"{n / 1_000_000_000.0:0.#}B",
            >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
            >= 1_000 => $"{n / 1_000.0:0.#}K",
            _ => n.ToString()
        };
        return $"{num} views";
    }

    // yt-dlp upload_date "YYYYMMDD" -> coarse relative age ("2 years ago"); "" when absent.
    private static string FormatUploadAge(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "NA" || raw.Length != 8) return "";
        if (!DateTime.TryParseExact(raw, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date))
            return "";
        var days = (DateTime.Now.Date - date.Date).TotalDays;
        if (days < 0) return "";
        if (days < 1) return "today";
        if (days < 2) return "yesterday";
        if (days < 7) return $"{(int)days} days ago";
        if (days < 30) { int w = (int)(days / 7); return $"{w} week{(w != 1 ? "s" : "")} ago"; }
        if (days < 365) { int m = (int)(days / 30); return $"{m} month{(m != 1 ? "s" : "")} ago"; }
        int y = (int)(days / 365); return $"{y} year{(y != 1 ? "s" : "")} ago";
    }

    public static async Task<string?> DownloadSingleVideoAsync(string videoUrl, string outputDir, string quality, bool audioOnly, bool embedThumbnail, IProgress<(int percent, string status)>? progress = null)
    {
        Directory.CreateDirectory(outputDir);
        string? outputFile = null;

        await Task.Run(() =>
        {
            string args;
            if (audioOnly)
            {
                string thumbArg = embedThumbnail ? "--embed-thumbnail" : "";
                args = $"-x --audio-format mp3 --audio-quality 0 {thumbArg} -o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" --newline \"{videoUrl}\"";
            }
            else
            {
                string formatArg = quality switch
                {
                    "720p" => "-f \"bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720]/best\"",
                    "480p" => "-f \"bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480]/best\"",
                    "360p" => "-f \"bestvideo[height<=360][ext=mp4]+bestaudio[ext=m4a]/best[height<=360]/best\"",
                    _ => "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best\""
                };
                args = $"{formatArg} --merge-output-format mp4 -o \"{Path.Combine(outputDir, "%(title)s.%(ext)s")}\" --newline \"{videoUrl}\"";
            }

            var psi = new ProcessStartInfo(FindYtDlpPath(), args)
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };

            using var proc = Process.Start(psi)!;
            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                var match = Regex.Match(e.Data, @"\[download\]\s+(\d+\.?\d*)%");
                if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pct))
                    progress?.Report(((int)pct, e.Data.Trim()));

                var destMatch = Regex.Match(e.Data, @"\[download\] Destination: (.+)$");
                if (destMatch.Success) outputFile = destMatch.Groups[1].Value.Trim();
                var mergeMatch = Regex.Match(e.Data, @"\[Merger\] Merging formats into ""(.+)""");
                if (mergeMatch.Success) outputFile = mergeMatch.Groups[1].Value.Trim();
            };
            proc.BeginOutputReadLine();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
                throw new Exception("Download failed");
        });

        return outputFile;
    }

    // =====================================================================
    //  Universal link downloader (any file via HTTP, or any media site via yt-dlp)
    // =====================================================================

    public enum LinkKind { DirectFile, Media }

    private static readonly string[] DirectFileExts =
    {
        ".jpg",".jpeg",".png",".gif",".webp",".bmp",".tif",".tiff",".svg",".ico",".heic",".avif",
        ".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".txt",".csv",".rtf",".odt",".ods",".odp",".epub",".mobi",
        ".zip",".rar",".7z",".tar",".gz",".bz2",".xz",".exe",".msi",".dmg",".pkg",".apk",".iso",".deb",".rpm",".appimage",
        ".mp3",".wav",".flac",".m4a",".aac",".ogg",".opus",".wma",
        ".mp4",".mkv",".webm",".mov",".avi",".flv",".wmv",".m4v",".ts",
        ".json",".xml",".yaml",".yml",".bin",".dll",".jar",".whl",".gguf",".safetensors",".onnx",
    };

    /// <summary>Heuristically classifies a URL: a path ending in a known file extension is a direct
    /// download; otherwise it's treated as a media page (handled by yt-dlp, e.g. Instagram / Facebook / X).</summary>
    public static LinkKind ClassifyUrl(string url)
    {
        try
        {
            string path = new Uri(url).AbsolutePath.ToLowerInvariant();
            foreach (var ext in DirectFileExts)
                if (path.EndsWith(ext)) return LinkKind.DirectFile;
        }
        catch { /* not a well-formed absolute URL */ }
        return LinkKind.Media;
    }

    /// <summary>Streams a direct file URL to <paramref name="outputDir"/> with progress (percent + a
    /// "12 MB / 40 MB · 8.4 MB/s" style status). Returns the saved path.</summary>
    public static async Task<string> DownloadDirectFileAsync(string url, string outputDir,
        IProgress<(int percent, string status)>? progress = null, System.Threading.CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromHours(6) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Llamashot/1.0");

        using var resp = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;

        string fileName = ResolveFileName(resp, url);
        string outPath = UniquePath(Path.Combine(outputDir, fileName));

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);
        var buf = new byte[1 << 20]; // 1 MB buffer
        long read = 0; int n;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long lastReport = 0;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (read - lastReport >= (1 << 20) || (total > 0 && read == total))
            {
                lastReport = read;
                double secs = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                string speed = FormatFileSize((long)(read / secs)) + "/s";
                int pct = total > 0 ? (int)(read * 100 / total) : 0;
                string status = total > 0
                    ? $"{FormatFileSize(read)} / {FormatFileSize(total)} · {speed}"
                    : $"{FormatFileSize(read)} · {speed}";
                progress?.Report((pct, status));
            }
        }
        progress?.Report((100, $"Done · {FormatFileSize(read)}"));
        return outPath;
    }

    private static string ResolveFileName(System.Net.Http.HttpResponseMessage resp, string url)
    {
        // Prefer Content-Disposition filename.
        var cd = resp.Content.Headers.ContentDisposition;
        string? name = cd?.FileNameStar ?? cd?.FileName;
        if (!string.IsNullOrWhiteSpace(name)) name = name.Trim('"');
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = Path.GetFileName(new Uri(url).AbsolutePath); } catch { name = null; }
        }
        if (string.IsNullOrWhiteSpace(name)) name = "download";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            string cand = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(cand)) return cand;
        }
    }

    /// <summary>Downloads any media-site URL (YouTube, Instagram, Facebook, X/Twitter, TikTok, …) via
    /// yt-dlp at best quality (or audio-only MP3). Returns the saved path.</summary>
    public static Task<string?> DownloadMediaAsync(string url, string outputDir, bool audioOnly,
        IProgress<(int percent, string status)>? progress = null)
        => DownloadSingleVideoAsync(url, outputDir, "best", audioOnly, embedThumbnail: false, progress);

    public static async Task ExtractAudioWithThumbnailAsync(string videoPath, string outputPath, IProgress<int>? progress = null)
    {
        var info = await GetVideoInfoAsync(videoPath);
        string ext = Path.GetExtension(outputPath).ToLowerInvariant();

        // First extract a thumbnail frame at 1 second
        string tempThumb = Path.Combine(Path.GetTempPath(), $"llamashot_thumb_{Guid.NewGuid():N}.jpg");
        try
        {
            await RunFFmpegAsync($"-ss 1 -i \"{videoPath}\" -vframes 1 -q:v 2 \"{tempThumb}\"", null, null);

            // Now extract audio with embedded thumbnail
            if (ext == ".mp3")
            {
                // MP3 supports ID3 album art
                await RunFFmpegAsync(
                    $"-i \"{videoPath}\" -i \"{tempThumb}\" -map 0:a -map 1:0 -c:a libmp3lame -q:a 0 -id3v2_version 3 -metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" \"{outputPath}\"",
                    info.duration, progress);
            }
            else
            {
                // For other formats, just extract audio without thumbnail
                string codec = ext switch
                {
                    ".wav" => "", ".aac" => "-acodec aac", ".flac" => "-acodec flac",
                    _ => "-acodec libmp3lame -q:a 0"
                };
                await RunFFmpegAsync($"-i \"{videoPath}\" -vn {codec} \"{outputPath}\"", info.duration, progress);
            }
        }
        finally
        {
            try { File.Delete(tempThumb); } catch { }
        }
    }

    public static bool IsImageExtension(string ext) =>
        ext.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".tif" or ".webp" or ".ico";

    public static bool IsOfficeExtension(string ext) =>
        ext.ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx" or ".odt" or ".ods" or ".odp";

    public static async Task<long> CompressFileAsync(string inputPath, string outputPath, int quality = 80, int maxDimension = 0, IProgress<int>? progress = null, string? password = null)
    {
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (IsImageExtension(ext))
            return await CompressImageAsync(inputPath, outputPath, quality, maxDimension);

        if (ext == ".pdf")
            return await CompressPdfAsync(inputPath, outputPath, quality, progress, password);

        if (IsOfficeExtension(ext))
            return await CompressOfficeDocAsync(inputPath, outputPath, quality, progress);

        return await CompressToZipAsync(inputPath, outputPath);
    }

    public static async Task<long> CompressPdfAsync(string inputPath, string outputPath, int quality = 80, IProgress<int>? progress = null, string? password = null)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
        var pdfDoc = await LoadPdfAsync(file, password);
        uint pageCount = pdfDoc.PageCount;

        string tempDir = Path.Combine(Path.GetTempPath(), "llamashot_pdf_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempImages = new List<string>();
            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var options = new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = (uint)(page.Size.Width * 150 / 72)
                };
                await page.RenderToStreamAsync(stream, options);
                stream.Seek(0);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream.AsStreamForRead();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                string tempFile = Path.Combine(tempDir, $"page_{i}.jpg");
                var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = new FileStream(tempFile, FileMode.Create);
                encoder.Save(fs);
                tempImages.Add(tempFile);

                progress?.Report((int)((i + 1) * 50 / pageCount));
            }

            var imgProgress = new Progress<int>(v => progress?.Report(50 + v / 2));
            await ImagesToPdfAsync(tempImages.ToArray(), outputPath, imgProgress);

            // Rasterizing pages only shrinks scanned/image-heavy PDFs. For text or
            // already-optimized documents it bloats the file — in that case keep the
            // original so "compress" never produces a larger result.
            long originalLen = new FileInfo(inputPath).Length;
            if (new FileInfo(outputPath).Length >= originalLen && !PathsEqual(inputPath, outputPath))
            {
                File.Copy(inputPath, outputPath, overwrite: true);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        return new FileInfo(outputPath).Length;
    }

    /// <summary>Result of a target-size compression: the bytes actually achieved and whether the target was met.</summary>
    public readonly record struct CompressResult(long Bytes, bool TargetMet);

    // Shrinks an image until it fits within <paramref name="targetBytes"/>. Lowers JPEG quality
    // first, then downscales resolution when quality alone can't get there. Best-effort: if even
    // the floor (min quality + min dimension) is over target, the smallest result is kept and
    // TargetMet is false. Never produces a file larger than the source.
    public static async Task<CompressResult> CompressImageToTargetAsync(string inputPath, string outputPath, long targetBytes, IProgress<int>? progress = null)
    {
        return await Task.Run(() =>
        {
            long originalLen = new FileInfo(inputPath).Length;
            if (originalLen <= targetBytes)
            {
                if (!PathsEqual(inputPath, outputPath)) File.Copy(inputPath, outputPath, overwrite: true);
                return new CompressResult(new FileInfo(outputPath).Length, true);
            }

            var original = LoadBitmapSourceFromFile(inputPath);
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            bool isPng = ext is ".png";

            byte[] Encode(BitmapSource src, int quality)
            {
                BitmapEncoder enc = isPng ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = quality };
                enc.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                enc.Save(ms);
                return ms.ToArray();
            }

            const int minQuality = 10, maxQuality = 92, minDimension = 400;
            BitmapSource current = original;
            byte[] best = Encode(current, minQuality); // smallest seen so far (floor of current res)

            for (int round = 0; round < 24; round++)
            {
                byte[] candidate;
                if (isPng)
                {
                    candidate = Encode(current, 0);
                }
                else
                {
                    candidate = Encode(current, minQuality);
                    if (candidate.Length <= targetBytes)
                    {
                        // The floor fits — binary-search the highest quality still within target.
                        byte[] fit = candidate;
                        int lo = minQuality + 1, hi = maxQuality;
                        while (lo <= hi)
                        {
                            int mid = (lo + hi) / 2;
                            var enc = Encode(current, mid);
                            if (enc.Length <= targetBytes) { fit = enc; lo = mid + 1; }
                            else hi = mid - 1;
                        }
                        candidate = fit;
                    }
                }

                if (candidate.Length < best.Length) best = candidate;
                progress?.Report(Math.Min(95, (round + 1) * 12));

                if (candidate.Length <= targetBytes)
                {
                    File.WriteAllBytes(outputPath, candidate);
                    return new CompressResult(candidate.Length, true);
                }

                int minSide = Math.Min(current.PixelWidth, current.PixelHeight);
                if (minSide <= minDimension) break; // can't shrink further — best effort

                var t = new TransformedBitmap(current, new ScaleTransform(0.85, 0.85));
                t.Freeze();
                current = t;
            }

            // Best effort: never exceed the original; pick the smaller of original and our best.
            if (best.Length >= originalLen && !PathsEqual(inputPath, outputPath))
            {
                File.Copy(inputPath, outputPath, overwrite: true);
                return new CompressResult(new FileInfo(outputPath).Length, originalLen <= targetBytes);
            }
            File.WriteAllBytes(outputPath, best);
            return new CompressResult(best.Length, best.Length <= targetBytes);
        });
    }

    // Shrinks a PDF until it fits within <paramref name="targetBytes"/> by rasterizing pages and
    // sweeping render DPI (high→low) with a per-DPI binary search on JPEG quality. Best-effort: if
    // nothing fits, the smallest result is kept (and never larger than the original).
    public static async Task<CompressResult> CompressPdfToTargetAsync(string inputPath, string outputPath, long targetBytes, IProgress<int>? progress = null, string? password = null)
    {
        long originalLen = new FileInfo(inputPath).Length;
        if (originalLen <= targetBytes)
        {
            if (!PathsEqual(inputPath, outputPath)) File.Copy(inputPath, outputPath, overwrite: true);
            return new CompressResult(new FileInfo(outputPath).Length, true);
        }

        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
        var pdfDoc = await LoadPdfAsync(file, password);
        uint pageCount = pdfDoc.PageCount;

        string tempDir = Path.Combine(Path.GetTempPath(), "llamashot_pdf_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        // Render every page ONCE at the base DPI here (WinRT rendering wants the UI thread);
        // lower DPIs are produced by cheap downscaling so we never re-render — this is what
        // kept the old version pinned to the UI thread and frozen for detailed documents.
        const int baseDpi = 150;
        var basePages = new List<BitmapSource>((int)pageCount);
        for (uint i = 0; i < pageCount; i++)
        {
            using var page = pdfDoc.GetPage(i);
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var options = new Windows.Data.Pdf.PdfPageRenderOptions
            {
                DestinationWidth = (uint)(page.Size.Width * baseDpi / 72)
            };
            await page.RenderToStreamAsync(stream, options);
            stream.Seek(0);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream.AsStreamForRead();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            basePages.Add(bmp);
            progress?.Report((int)((i + 1) * 30 / pageCount));
        }

        // The encode/assemble/search loop is CPU-bound — run it off the UI thread so the
        // window stays responsive (the spinner keeps animating).
        return await Task.Run(() =>
        {
            string candPdf = Path.Combine(tempDir, "cand.pdf");
            string bestPdf = Path.Combine(tempDir, "best.pdf");
            int[] dpis = { 150, 120, 96, 72, 50 };
            const int minQuality = 10, maxQuality = 85;
            long smallestLen = long.MaxValue;

            try
            {
                List<BitmapSource> PagesAt(int dpi)
                {
                    if (dpi == baseDpi) return basePages;
                    double scale = (double)dpi / baseDpi;
                    var list = new List<BitmapSource>(basePages.Count);
                    foreach (var src in basePages)
                    {
                        var t = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                        t.Freeze();
                        list.Add(t);
                    }
                    return list;
                }

                long Assemble(List<BitmapSource> pages, int quality)
                {
                    var imgs = new string[pages.Count];
                    for (int i = 0; i < pages.Count; i++)
                    {
                        string f = Path.Combine(tempDir, $"p_{i}.jpg");
                        var enc = new JpegBitmapEncoder { QualityLevel = quality };
                        enc.Frames.Add(BitmapFrame.Create(pages[i]));
                        using (var fsp = new FileStream(f, FileMode.Create)) enc.Save(fsp);
                        imgs[i] = f;
                    }
                    ImagesToPdfCore(imgs, candPdf, null);
                    return new FileInfo(candPdf).Length;
                }

                for (int d = 0; d < dpis.Length; d++)
                {
                    var pages = PagesAt(dpis[d]);

                    // Floor for this DPI: if even min quality is over target, drop to a lower DPI.
                    long floorLen = Assemble(pages, minQuality);
                    if (floorLen < smallestLen) { smallestLen = floorLen; File.Copy(candPdf, bestPdf, true); }
                    progress?.Report(Math.Min(95, 40 + (d + 1) * 11));

                    if (floorLen > targetBytes) continue;

                    // Floor fits — binary-search the highest quality still within target.
                    long bestFitLen = floorLen;
                    File.Copy(candPdf, bestPdf, true);
                    int lo = minQuality + 1, hi = maxQuality;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        long len = Assemble(pages, mid);
                        if (len <= targetBytes) { bestFitLen = len; File.Copy(candPdf, bestPdf, true); lo = mid + 1; }
                        else hi = mid - 1;
                    }

                    if (bestFitLen >= originalLen && !PathsEqual(inputPath, outputPath))
                    {
                        File.Copy(inputPath, outputPath, true);
                        return new CompressResult(new FileInfo(outputPath).Length, false);
                    }
                    File.Copy(bestPdf, outputPath, true);
                    return new CompressResult(bestFitLen, true);
                }

                // Nothing fit at any DPI: keep the smaller of the best rasterization and the original.
                if (smallestLen >= originalLen && !PathsEqual(inputPath, outputPath))
                {
                    File.Copy(inputPath, outputPath, true);
                    return new CompressResult(new FileInfo(outputPath).Length, false);
                }
                File.Copy(bestPdf, outputPath, true);
                return new CompressResult(smallestLen, false);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        });
    }

    public static async Task<long> CompressOfficeDocAsync(string inputPath, string outputPath, int quality = 80, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
        {
            using var inputArchive = ZipFile.OpenRead(inputPath);
            var entries = inputArchive.Entries.ToList();
            int count = entries.Count;

            using var outputFs = new FileStream(outputPath, FileMode.Create);
            using var outputArchive = new ZipArchive(outputFs, ZipArchiveMode.Create);

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];

                if (entry.Length == 0)
                {
                    outputArchive.CreateEntry(entry.FullName);
                    progress?.Report((i + 1) * 100 / count);
                    continue;
                }

                var newEntry = outputArchive.CreateEntry(entry.FullName, CompressionLevel.SmallestSize);
                newEntry.LastWriteTime = entry.LastWriteTime;
                string entryExt = Path.GetExtension(entry.Name).ToLowerInvariant();

                if (IsImageExtension(entryExt) && entry.Length > 1024)
                {
                    try
                    {
                        using var entryStream = entry.Open();
                        using var ms = new MemoryStream();
                        entryStream.CopyTo(ms);
                        byte[] originalBytes = ms.ToArray();
                        ms.Position = 0;

                        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                        var frame = decoder.Frames[0];

                        var jpegEncoder = new JpegBitmapEncoder { QualityLevel = quality };
                        jpegEncoder.Frames.Add(BitmapFrame.Create(frame));
                        using var compressedMs = new MemoryStream();
                        jpegEncoder.Save(compressedMs);
                        byte[] compressedBytes = compressedMs.ToArray();

                        byte[] toWrite = compressedBytes.Length < originalBytes.Length ? compressedBytes : originalBytes;
                        using var newStream = newEntry.Open();
                        newStream.Write(toWrite, 0, toWrite.Length);
                    }
                    catch
                    {
                        using var src = entry.Open();
                        using var dst = newEntry.Open();
                        src.CopyTo(dst);
                    }
                }
                else
                {
                    using var src = entry.Open();
                    using var dst = newEntry.Open();
                    src.CopyTo(dst);
                }

                progress?.Report((i + 1) * 100 / count);
            }
        });

        return new FileInfo(outputPath).Length;
    }

    public static async Task<long> CompressToZipAsync(string inputPath, string outputPath)
    {
        await Task.Run(() =>
        {
            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(inputPath, Path.GetFileName(inputPath), CompressionLevel.SmallestSize);
        });
        return new FileInfo(outputPath).Length;
    }

    public static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
            _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
        };
    }

    private static string CreateTempDir(string prefix)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"llamashot_{prefix}_{Guid.NewGuid():N}"[..30]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task<BitmapSource> RenderPdfPageAsync(Windows.Data.Pdf.PdfPage page, int dpi = 150)
    {
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
        return bmp;
    }

    /// <summary>Parses a "#RRGGBB" hex color, falling back to <paramref name="fallback"/> on failure.</summary>
    private static Color ParseColor(string? hex, Color fallback)
    {
        try { if (!string.IsNullOrWhiteSpace(hex)) return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); } catch { }
        return fallback;
    }

    /// <summary>Maps a watermark orientation key to a rotation angle (degrees).</summary>
    private static double AngleFor(string orientation) => orientation switch
    {
        "horizontal" => 0,
        "vertical" => -90,
        "diagonal-down" => 45,   // "\"
        _ => -45,                // "diagonal-up"  "/"
    };

    /// <summary>Computes the top-left draw point for a text block given a 9-position key.</summary>
    private static (double x, double y) PlaceText(string position, double w, double h, double tw, double th, double margin) => position switch
    {
        "top-left" => (margin, margin),
        "top-center" => ((w - tw) / 2, margin),
        "top-right" => (w - tw - margin, margin),
        "center" => ((w - tw) / 2, (h - th) / 2),
        "bottom-left" => (margin, h - th - margin),
        "bottom-right" => (w - tw - margin, h - th - margin),
        _ => ((w - tw) / 2, h - th - margin), // bottom-center
    };

    private static BitmapSource RenderOverlay(BitmapSource source, Action<DrawingContext, int, int> drawAction)
    {
        int w = source.PixelWidth;
        int h = source.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, w, h));
            drawAction(dc, w, h);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource LoadBitmapSourceFromFile(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static void SaveBitmapSource(BitmapSource source, string outputPath)
    {
        string ext = Path.GetExtension(outputPath).ToLowerInvariant();
        var encoder = GetBitmapEncoder(ext);
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }
}

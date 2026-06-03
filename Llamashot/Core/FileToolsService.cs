using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

public static class FileToolsService
{
    public static async Task ImagesToPdfAsync(string[] imagePaths, string outputPath, IProgress<int>? progress = null)
    {
        await Task.Run(() =>
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
        });
    }

    public static async Task<string[]> PdfToImagesAsync(string pdfPath, string outputDir, string format = "png", int dpi = 150, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(outputDir);

        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
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
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        });

        return new FileInfo(outputPath).Length;
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

    public static async Task<int> GetPdfPageCountAsync(string pdfPath)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
        return (int)pdfDoc.PageCount;
    }

    public static async Task MergePdfsAsync(string[] pdfPaths, string outputPath, IProgress<int>? progress = null)
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
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPaths[f]);
                var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
                pageCounts[f] = (int)pdfDoc.PageCount;
                totalPages += pageCounts[f];
            }

            int pagesProcessed = 0;

            // Second pass: render all pages
            for (int f = 0; f < pdfPaths.Length; f++)
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPaths[f]);
                var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

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

    public static async Task<string[]> SplitPdfAsync(string pdfPath, string outputDir, int fromPage, int toPage, IProgress<int>? progress = null)
    {
        Directory.CreateDirectory(outputDir);
        string tempDir = CreateTempDir("split");

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

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

    public static async Task RotatePdfAsync(string pdfPath, string outputPath, int degrees, IProgress<int>? progress = null)
    {
        string tempDir = CreateTempDir("rotate");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
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

    public static async Task WatermarkPdfAsync(string pdfPath, string outputPath, string watermarkText, double opacity = 0.3, int fontSize = 48, IProgress<int>? progress = null)
    {
        string tempDir = CreateTempDir("watermark");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
            uint pageCount = pdfDoc.PageCount;

            var tempImages = new List<string>();

            for (uint i = 0; i < pageCount; i++)
            {
                using var page = pdfDoc.GetPage(i);
                var bmp = await RenderPdfPageAsync(page);

                string tempFile = Path.Combine(tempDir, $"page_{i:D3}.jpg");
                int capturedFontSize = fontSize;
                double capturedOpacity = opacity;
                string capturedText = watermarkText;

                var thread = new Thread(() =>
                {
                    var watermarked = RenderOverlay(bmp, (dc, w, h) =>
                    {
                        dc.PushOpacity(capturedOpacity);
                        dc.PushTransform(new RotateTransform(-45, w / 2.0, h / 2.0));

                        var formattedText = new FormattedText(
                            capturedText,
                            CultureInfo.InvariantCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"),
                            capturedFontSize,
                            Brushes.Gray,
                            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

                        double textX = (w - formattedText.Width) / 2;
                        double textY = (h - formattedText.Height) / 2;
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

    public static async Task AddPageNumbersAsync(string pdfPath, string outputPath, string position = "bottom-center", IProgress<int>? progress = null)
    {
        string tempDir = CreateTempDir("pagenums");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
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

                var thread = new Thread(() =>
                {
                    string pageText = $"Page {capturedI + 1} of {capturedCount}";

                    var numbered = RenderOverlay(bmp, (dc, w, h) =>
                    {
                        var formattedText = new FormattedText(
                            pageText,
                            CultureInfo.InvariantCulture,
                            System.Windows.FlowDirection.LeftToRight,
                            new Typeface("Segoe UI"),
                            14,
                            new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                            VisualTreeHelper.GetDpi(new DrawingVisual()).PixelsPerDip);

                        double textX, textY;
                        double margin = 20;

                        switch (capturedPosition)
                        {
                            case "bottom-right":
                                textX = w - formattedText.Width - margin;
                                textY = h - formattedText.Height - margin;
                                break;
                            case "top-center":
                                textX = (w - formattedText.Width) / 2;
                                textY = margin;
                                break;
                            case "top-right":
                                textX = w - formattedText.Width - margin;
                                textY = margin;
                                break;
                            default: // bottom-center
                                textX = (w - formattedText.Width) / 2;
                                textY = h - formattedText.Height - margin;
                                break;
                        }

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

    public static bool IsImageExtension(string ext) =>
        ext.ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".tif" or ".webp" or ".ico";

    public static bool IsOfficeExtension(string ext) =>
        ext.ToLowerInvariant() is ".docx" or ".xlsx" or ".pptx" or ".odt" or ".ods" or ".odp";

    public static async Task<long> CompressFileAsync(string inputPath, string outputPath, int quality = 80, int maxDimension = 0, IProgress<int>? progress = null)
    {
        string ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (IsImageExtension(ext))
            return await CompressImageAsync(inputPath, outputPath, quality, maxDimension);

        if (ext == ".pdf")
            return await CompressPdfAsync(inputPath, outputPath, quality, progress);

        if (IsOfficeExtension(ext))
            return await CompressOfficeDocAsync(inputPath, outputPath, quality, progress);

        return await CompressToZipAsync(inputPath, outputPath);
    }

    public static async Task<long> CompressPdfAsync(string inputPath, string outputPath, int quality = 80, IProgress<int>? progress = null)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(inputPath);
        var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
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
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }

        return new FileInfo(outputPath).Length;
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

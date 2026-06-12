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

    public static async Task ExtractPdfPagesAsync(string pdfPath, int[] pageNumbers, string outputPath, IProgress<int>? progress = null)
    {
        string tempDir = CreateTempDir("extract");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

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

    public static async Task InsertPdfPagesAsync(string basePdfPath, string[] insertImagePaths, int afterPage, string outputPath, IProgress<int>? progress = null)
    {
        string tempDir = CreateTempDir("insert");
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(basePdfPath);
            var pdfDoc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
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

            // Build ordered list: base pages 1..afterPage, then insert images, then remaining base pages
            var allImages = new List<string>();
            for (int i = 0; i < afterPage; i++)
                allImages.Add(tempImages[i]);

            allImages.AddRange(insertImagePaths);

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
                $"-v error -select_streams v:0 -show_entries stream=width,height,codec_name -show_entries format=duration -of csv=p=0:s=, \"{path}\"")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            string output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(10000);

            // Output format: "width,height,codec\nduration"
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int width = 0, height = 0;
            string codec = "unknown";
            double durationSec = 0;

            if (lines.Length >= 1)
            {
                var parts = lines[0].Split(',');
                if (parts.Length >= 1) int.TryParse(parts[0].Trim(), out width);
                if (parts.Length >= 2) int.TryParse(parts[1].Trim(), out height);
                if (parts.Length >= 3) codec = parts[2].Trim();
            }
            if (lines.Length >= 2)
                double.TryParse(lines[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out durationSec);

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
        string startStr = start.ToString(@"hh\:mm\:ss\.ff");
        string endStr = end.ToString(@"hh\:mm\:ss\.ff");
        var duration = end - start;
        await RunFFmpegAsync($"-i \"{inputPath}\" -ss {startStr} -to {endStr} -c copy \"{outputPath}\"", duration, progress);
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

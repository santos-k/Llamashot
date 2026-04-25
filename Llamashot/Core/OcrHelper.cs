using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Llamashot.Core;

public static class OcrHelper
{
    public static async Task<string> ExtractTextAsync(BitmapSource bitmapSource)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            var enLang = new Windows.Globalization.Language("en-US");
            if (OcrEngine.IsLanguageSupported(enLang))
                engine = OcrEngine.TryCreateFromLanguage(enLang);
        }
        if (engine == null)
        {
            var available = OcrEngine.AvailableRecognizerLanguages;
            if (available.Count > 0)
                engine = OcrEngine.TryCreateFromLanguage(available[0]);
        }
        if (engine == null)
            return "[OCR not available — no language pack installed]";

        // Preprocess: convert to high-contrast image for better OCR
        var processed = PreprocessForOcr(bitmapSource);

        // Try OCR on processed image
        var softwareBitmap = await ConvertViaTempFileAsync(processed);
        var result = await engine.RecognizeAsync(softwareBitmap);

        // If no result on processed, try original too
        if (string.IsNullOrWhiteSpace(result.Text))
        {
            var origBitmap = await ConvertViaTempFileAsync(bitmapSource);
            var origResult = await engine.RecognizeAsync(origBitmap);
            if (!string.IsNullOrWhiteSpace(origResult.Text))
                return origResult.Text;
        }

        return result.Text;
    }

    private static BitmapSource PreprocessForOcr(BitmapSource source)
    {
        // Convert to Bgra32
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();

        int w = converted.PixelWidth;
        int h = converted.PixelHeight;
        int stride = w * 4;
        byte[] pixels = new byte[stride * h];
        converted.CopyPixels(pixels, stride, 0);

        // Calculate average brightness
        long totalBrightness = 0;
        int pixelCount = w * h;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
            totalBrightness += (r + g + b) / 3;
        }
        double avgBrightness = (double)totalBrightness / pixelCount;

        // If dark background (avg brightness < 128), invert colors
        bool isDark = avgBrightness < 128;
        if (isDark)
        {
            for (int i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = (byte)(255 - pixels[i]);       // B
                pixels[i + 1] = (byte)(255 - pixels[i + 1]); // G
                pixels[i + 2] = (byte)(255 - pixels[i + 2]); // R
                // Alpha stays the same
            }
        }

        // Increase contrast
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = Contrast(pixels[i]);
            pixels[i + 1] = Contrast(pixels[i + 1]);
            pixels[i + 2] = Contrast(pixels[i + 2]);
        }

        var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        // Scale up small images — OCR works better with larger text
        if (w < 300 || h < 50)
        {
            double scale = Math.Max(300.0 / w, 50.0 / h);
            scale = Math.Min(scale, 4.0); // cap at 4x
            var scaled = new TransformedBitmap(result,
                new System.Windows.Media.ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }

        result.Freeze();
        return result;
    }

    private static byte Contrast(byte value)
    {
        // Increase contrast: push values away from middle
        double v = value / 255.0;
        v = (v - 0.5) * 1.5 + 0.5; // 1.5x contrast
        return (byte)Math.Clamp((int)(v * 255), 0, 255);
    }

    private static async Task<SoftwareBitmap> ConvertViaTempFileAsync(BitmapSource source)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"llamashot_ocr_{Guid.NewGuid():N}.png");
        try
        {
            // Ensure source is frozen/readable
            BitmapSource saveable = source;
            if (!source.IsFrozen)
            {
                var clone = source.Clone();
                clone.Freeze();
                saveable = clone;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(saveable));
            using (var fs = File.Create(tempPath))
                encoder.Save(fs);

            var storageFile = await StorageFile.GetFileFromPathAsync(tempPath);
            using var stream = await storageFile.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            return softwareBitmap;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }
}

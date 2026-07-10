using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Llamashot.Core.Ocr;

/// <summary>Shared image helpers for the OCR engines (encoding + light preprocessing).</summary>
internal static class OcrImaging
{
    /// <summary>Encodes a bitmap to in-memory PNG bytes (lossless — best for OCR input).</summary>
    public static byte[] EncodePng(BitmapSource source)
    {
        BitmapSource saveable = source;
        if (!source.IsFrozen)
        {
            var clone = source.Clone();
            clone.Freeze();
            saveable = clone;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(saveable));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Light preprocessing that helps both engines: upscale small/low-resolution images so text
    /// is large enough to segment. Returns the source unchanged when already big enough.
    /// </summary>
    public static BitmapSource Prepare(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        // Upscale when the page is small — OCR accuracy drops sharply below ~1000px wide.
        if (w > 0 && w < 1000)
        {
            double scale = Math.Min(3.0, 1000.0 / w);
            if (scale > 1.05)
            {
                var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
                scaled.Freeze();
                return scaled;
            }
        }
        return source;
    }

    /// <summary>Converts a WPF bitmap to a WinRT SoftwareBitmap via a temp PNG (for Windows.Media.Ocr).</summary>
    public static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource source)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"llamashot_ocr_{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(tempPath, EncodePng(source));
            var storageFile = await StorageFile.GetFileFromPathAsync(tempPath);
            using var stream = await storageFile.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }
}

using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Llamashot.Core;

public static class OcrHelper
{
    public static async Task<string> ExtractTextAsync(BitmapSource bitmapSource)
    {
        // Try user profile languages first, fall back to English
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            var enLang = new Windows.Globalization.Language("en-US");
            if (OcrEngine.IsLanguageSupported(enLang))
                engine = OcrEngine.TryCreateFromLanguage(enLang);
        }
        if (engine == null)
        {
            // Try any available language
            var available = OcrEngine.AvailableRecognizerLanguages;
            if (available.Count > 0)
                engine = OcrEngine.TryCreateFromLanguage(available[0]);
        }
        if (engine == null)
            return "[OCR not available — no language pack installed]";

        var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmapSource);
        var result = await engine.RecognizeAsync(softwareBitmap);

        return result.Text;
    }

    private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(BitmapSource source)
    {
        // Ensure we have a format we can read pixels from
        var convertedSource = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = PixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            convertedSource = converted;
        }

        int width = convertedSource.PixelWidth;
        int height = convertedSource.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        convertedSource.CopyPixels(pixels, stride, 0);

        // Create SoftwareBitmap directly from pixel data
        var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);

        using (var buffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Write))
        {
            using (var reference = buffer.CreateReference())
            {
                unsafe
                {
                    byte* dataPtr;
                    uint capacity;
                    ((IMemoryBufferByteAccess)reference).GetBuffer(out dataPtr, out capacity);

                    // Copy pixel data
                    for (int i = 0; i < pixels.Length && i < capacity; i++)
                    {
                        dataPtr[i] = pixels[i];
                    }
                }
            }
        }

        return softwareBitmap;
    }

    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}

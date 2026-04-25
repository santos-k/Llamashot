using System.IO;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Llamashot.Core;

public static class OcrHelper
{
    public static async Task<string> ExtractTextAsync(BitmapSource bitmapSource)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
            return "[OCR not available — no language pack installed]";

        var softwareBitmap = await ConvertToSoftwareBitmapAsync(bitmapSource);
        var result = await engine.RecognizeAsync(softwareBitmap);

        return result.Text;
    }

    private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(BitmapSource source)
    {
        // Encode WPF BitmapSource to PNG in memory
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));

        using var memStream = new MemoryStream();
        encoder.Save(memStream);
        memStream.Position = 0;

        // Convert to WinRT IRandomAccessStream
        var winrtStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var outputStream = winrtStream.GetOutputStreamAt(0);
        var writer = new Windows.Storage.Streams.DataWriter(outputStream);
        writer.WriteBytes(memStream.ToArray());
        await writer.StoreAsync();
        await outputStream.FlushAsync();
        winrtStream.Seek(0);

        // Decode to SoftwareBitmap
        var decoder = await BitmapDecoder.CreateAsync(winrtStream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        return softwareBitmap;
    }
}

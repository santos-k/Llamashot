using System.Windows.Media.Imaging;
using Llamashot.Core.Ocr;

namespace Llamashot.Core;

/// <summary>
/// Convenience entry point for screenshot OCR (extract text from a captured region).
/// Delegates to the shared engine layer; uses the built-in Windows engine for fast, asset-free
/// recognition. For file OCR (images / PDFs, most-accurate Tesseract) see <see cref="OcrService"/>.
/// </summary>
public static class OcrHelper
{
    private static readonly WindowsOcrEngine Engine = new();

    public static async Task<string> ExtractTextAsync(BitmapSource bitmapSource)
    {
        if (!Engine.IsAvailable)
            return "[OCR not available — no language pack installed]";

        var page = await Engine.RecognizeAsync(bitmapSource, langCode: "");
        if (page.Error != null)
            return $"[{page.Error}]";
        return page.Text;
    }
}

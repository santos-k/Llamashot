namespace Llamashot.Core.Ocr;

/// <summary>Registry of available OCR engines. Tesseract is preferred when its assets are present.</summary>
public static class OcrEngines
{
    private static readonly IOcrEngine[] All =
    {
        new TesseractOcrEngine(),
        new WindowsOcrEngine(),
    };

    /// <summary>All engines that are usable on this machine, most-accurate first.</summary>
    public static IReadOnlyList<IOcrEngine> Available => All.Where(e => e.IsAvailable).ToList();

    /// <summary>The best available engine (Tesseract if present, else Windows), or null if none.</summary>
    public static IOcrEngine? Default => All.FirstOrDefault(e => e.IsAvailable);

    /// <summary>Finds an available engine by id, falling back to the default.</summary>
    public static IOcrEngine? ById(string? id)
        => All.FirstOrDefault(e => e.IsAvailable && e.Id == id) ?? Default;
}

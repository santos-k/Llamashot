using System.Windows.Media.Imaging;

namespace Llamashot.Core.Ocr;

/// <summary>An OCR backend. Implementations must be safe to call from a background thread.</summary>
public interface IOcrEngine
{
    /// <summary>Stable identifier used in settings/UI (e.g. "tesseract", "windows").</summary>
    string Id { get; }

    /// <summary>Human-readable name shown in the engine picker.</summary>
    string DisplayName { get; }

    /// <summary>True when this engine's runtime assets are present and usable on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Languages this engine can currently recognize (e.g. installed trained data / packs).</summary>
    IReadOnlyList<OcrLanguageOption> GetLanguages();

    /// <summary>Recognizes text in <paramref name="image"/> using the engine-native language <paramref name="langCode"/>.</summary>
    Task<OcrPage> RecognizeAsync(BitmapSource image, string langCode, CancellationToken ct = default);
}

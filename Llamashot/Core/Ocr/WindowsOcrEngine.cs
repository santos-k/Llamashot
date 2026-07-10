using System.Windows;
using System.Windows.Media.Imaging;
using Windows.Media.Ocr;

namespace Llamashot.Core.Ocr;

/// <summary>OCR backed by the built-in Windows.Media.Ocr engine. Always available on Win10+, offline, no assets.</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    public string Id => "windows";
    public string DisplayName => "Windows (built-in)";

    public bool IsAvailable
    {
        get
        {
            try { return OcrEngine.AvailableRecognizerLanguages.Count > 0; }
            catch { return false; }
        }
    }

    public IReadOnlyList<OcrLanguageOption> GetLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => new OcrLanguageOption(l.LanguageTag, l.DisplayName))
                .ToList();
        }
        catch
        {
            return new List<OcrLanguageOption>();
        }
    }

    public async Task<OcrPage> RecognizeAsync(BitmapSource image, string langCode, CancellationToken ct = default)
    {
        var prepared = OcrImaging.Prepare(image);

        var engine = ResolveEngine(langCode);
        if (engine == null)
            return new OcrPage { PixelWidth = prepared.PixelWidth, PixelHeight = prepared.PixelHeight, Error = "No Windows OCR language pack installed." };

        ct.ThrowIfCancellationRequested();
        var softwareBitmap = await OcrImaging.ToSoftwareBitmapAsync(prepared);
        var result = await engine.RecognizeAsync(softwareBitmap);

        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            var words = new List<OcrWord>();
            foreach (var w in line.Words)
            {
                var r = w.BoundingRect;
                words.Add(new OcrWord
                {
                    Text = w.Text,
                    Box = new Rect(r.X, r.Y, r.Width, r.Height),
                    Confidence = 100f, // Windows OCR exposes no per-word confidence
                });
            }
            var box = words.Count > 0 ? Union(words) : Rect.Empty;
            lines.Add(new OcrLine { Text = line.Text, Words = words, Box = box });
        }

        return new OcrPage
        {
            Text = string.Join("\n", lines.Select(l => l.Text)),
            Lines = lines,
            PixelWidth = prepared.PixelWidth,
            PixelHeight = prepared.PixelHeight,
        };
    }

    private static OcrEngine? ResolveEngine(string langCode)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(langCode))
            {
                var lang = new Windows.Globalization.Language(langCode);
                if (OcrEngine.IsLanguageSupported(lang))
                    return OcrEngine.TryCreateFromLanguage(lang);
            }
        }
        catch { /* fall through to profile default */ }

        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    private static Rect Union(List<OcrWord> words)
    {
        var r = words[0].Box;
        for (int i = 1; i < words.Count; i++) r.Union(words[i].Box);
        return r;
    }
}

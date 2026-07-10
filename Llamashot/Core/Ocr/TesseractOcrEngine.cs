using System.IO;
using System.Windows.Media.Imaging;
using Tesseract;

namespace Llamashot.Core.Ocr;

/// <summary>
/// OCR backed by the bundled Tesseract 5 LSTM engine. Most accurate of the offline engines.
/// Native libs ship in the app's x64\ folder; trained data in the app's tessdata\ folder.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    public string Id => "tesseract";
    public string DisplayName => "Tesseract 5 (most accurate)";

    private static string TessDataPath => Path.Combine(AppContext.BaseDirectory, "tessdata");

    // Friendly names for the language codes we may ship. Unknown codes fall back to the raw code.
    private static readonly Dictionary<string, string> LangNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng"] = "English", ["spa"] = "Spanish", ["fra"] = "French", ["deu"] = "German",
        ["ita"] = "Italian", ["por"] = "Portuguese", ["nld"] = "Dutch", ["rus"] = "Russian",
        ["hin"] = "Hindi", ["jpn"] = "Japanese", ["kor"] = "Korean", ["ara"] = "Arabic",
        ["chi_sim"] = "Chinese (Simplified)", ["chi_tra"] = "Chinese (Traditional)",
    };

    static TesseractOcrEngine()
    {
        // Under single-file publish the native loader can't infer the x64\ folder location;
        // point it at the app base directory (which contains x64\leptonica*.dll + tesseract50.dll).
        try { TesseractEnviornment.CustomSearchPath = AppContext.BaseDirectory; }
        catch { /* best effort */ }
    }

    public bool IsAvailable
    {
        get
        {
            try { return File.Exists(Path.Combine(TessDataPath, "eng.traineddata")); }
            catch { return false; }
        }
    }

    public IReadOnlyList<OcrLanguageOption> GetLanguages()
    {
        try
        {
            return Directory.EnumerateFiles(TessDataPath, "*.traineddata")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(c => !string.IsNullOrEmpty(c) && !string.Equals(c, "osd", StringComparison.OrdinalIgnoreCase))
                .Select(c => new OcrLanguageOption(c!, LangNames.GetValueOrDefault(c!, c!)))
                .OrderBy(o => o.Display)
                .ToList();
        }
        catch
        {
            return new List<OcrLanguageOption>();
        }
    }

    public Task<OcrPage> RecognizeAsync(BitmapSource image, string langCode, CancellationToken ct = default)
    {
        var prepared = OcrImaging.Prepare(image);
        int w = prepared.PixelWidth, h = prepared.PixelHeight;
        byte[] png = OcrImaging.EncodePng(prepared);
        string lang = string.IsNullOrWhiteSpace(langCode) ? "eng" : langCode;

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var engine = new TesseractEngine(TessDataPath, lang, EngineMode.LstmOnly);
            engine.DefaultPageSegMode = PageSegMode.Auto;

            using var pix = Pix.LoadFromMemory(png);
            using var page = engine.Process(pix);

            string fullText = page.GetText() ?? "";
            var lines = ExtractLines(page);

            return new OcrPage
            {
                Text = fullText.Replace("\r\n", "\n").TrimEnd('\n'),
                Lines = lines,
                PixelWidth = w,
                PixelHeight = h,
            };
        }, ct);
    }

    /// <summary>Walks the result iterator block→para→line→word to build structured lines with word boxes.</summary>
    private static List<OcrLine> ExtractLines(Page page)
    {
        var lines = new List<OcrLine>();
        using var iter = page.GetIterator();
        iter.Begin();
        do // block
        {
            do // paragraph
            {
                do // text line
                {
                    var words = new List<OcrWord>();
                    do // word
                    {
                        string text = iter.GetText(PageIteratorLevel.Word) ?? "";
                        if (text.Length == 0) continue;
                        float conf = iter.GetConfidence(PageIteratorLevel.Word);
                        var box = iter.TryGetBoundingBox(PageIteratorLevel.Word, out var r)
                            ? new System.Windows.Rect(r.X1, r.Y1, r.Width, r.Height)
                            : System.Windows.Rect.Empty;
                        words.Add(new OcrWord { Text = text, Box = box, Confidence = conf });
                    }
                    while (iter.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word));

                    if (words.Count > 0)
                    {
                        var lineBox = words[0].Box;
                        for (int i = 1; i < words.Count; i++) lineBox.Union(words[i].Box);
                        lines.Add(new OcrLine
                        {
                            Text = string.Join(" ", words.Select(x => x.Text)),
                            Words = words,
                            Box = lineBox,
                        });
                    }
                }
                while (iter.Next(PageIteratorLevel.Para, PageIteratorLevel.TextLine));
            }
            while (iter.Next(PageIteratorLevel.Block, PageIteratorLevel.Para));
        }
        while (iter.Next(PageIteratorLevel.Block));

        return lines;
    }
}

namespace Llamashot.Core.Ocr;

/// <summary>One recognized word with its pixel bounding box (top-left origin) in the page image.</summary>
public sealed class OcrWord
{
    public required string Text { get; init; }
    public System.Windows.Rect Box { get; init; }
    /// <summary>0–100 confidence where the engine provides one; 100 otherwise.</summary>
    public float Confidence { get; init; } = 100f;
}

/// <summary>One recognized line: its joined text and the words it contains.</summary>
public sealed class OcrLine
{
    public required string Text { get; init; }
    public List<OcrWord> Words { get; init; } = new();
    public System.Windows.Rect Box { get; init; }
}

/// <summary>OCR result for a single page/image: full text plus structured lines/words.</summary>
public sealed class OcrPage
{
    public string Text { get; init; } = "";
    public List<OcrLine> Lines { get; init; } = new();
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    /// <summary>Set when this page failed to recognize; <see cref="Text"/> is then empty.</summary>
    public string? Error { get; init; }
}

/// <summary>A selectable OCR language: the engine-native code plus a friendly name.</summary>
public sealed record OcrLanguageOption(string Code, string Display);

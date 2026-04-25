using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

public class ScreenshotRecord
{
    public string FilePath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public static class HistoryManager
{
    private static readonly string IndexPath = Path.Combine(
        AppSettings.Instance.HistoryDirectory, "history.json");
    private static List<ScreenshotRecord> _records = new();

    public static IReadOnlyList<ScreenshotRecord> Records => _records.AsReadOnly();

    public static void Load()
    {
        if (!File.Exists(IndexPath))
        {
            _records = new List<ScreenshotRecord>();
            return;
        }
        try
        {
            var json = File.ReadAllText(IndexPath);
            _records = JsonSerializer.Deserialize<List<ScreenshotRecord>>(json) ?? new();
        }
        catch
        {
            _records = new();
        }
    }

    public static void AddRecord(BitmapSource image, string savedPath)
    {
        if (!AppSettings.Instance.SaveHistory) return;

        var dir = AppSettings.Instance.HistoryDirectory;
        var thumbDir = Path.Combine(dir, "thumbnails");
        Directory.CreateDirectory(thumbDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var thumbPath = Path.Combine(thumbDir, $"thumb_{timestamp}.png");

        // Create thumbnail (200px wide)
        var scale = 200.0 / image.PixelWidth;
        var thumbHeight = (int)(image.PixelHeight * scale);
        var thumb = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(thumb));
        using (var fs = File.Create(thumbPath))
            encoder.Save(fs);

        var record = new ScreenshotRecord
        {
            FilePath = savedPath,
            ThumbnailPath = thumbPath,
            CapturedAt = DateTime.Now,
            Width = image.PixelWidth,
            Height = image.PixelHeight
        };

        _records.Insert(0, record);

        // Trim to max
        while (_records.Count > AppSettings.Instance.MaxHistoryItems)
        {
            var old = _records[^1];
            try { File.Delete(old.ThumbnailPath); } catch { }
            _records.RemoveAt(_records.Count - 1);
        }

        Save();
    }

    private static void Save()
    {
        Directory.CreateDirectory(AppSettings.Instance.HistoryDirectory);
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(IndexPath, JsonSerializer.Serialize(_records, options));
    }

    public static void Clear()
    {
        foreach (var r in _records)
        {
            try { File.Delete(r.ThumbnailPath); } catch { }
        }
        _records.Clear();
        Save();
    }
}

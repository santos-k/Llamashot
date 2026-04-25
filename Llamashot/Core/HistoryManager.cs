using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

public enum RecordType { Saved, Clipboard }

public class ScreenshotRecord
{
    public string FilePath { get; set; } = "";
    public string ThumbnailPath { get; set; } = "";
    public DateTime CapturedAt { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public RecordType Type { get; set; } = RecordType.Saved;
}

public static class HistoryManager
{
    private static string IndexPath => Path.Combine(
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
        AddEntry(image, savedPath, RecordType.Saved);
    }

    public static void AddClipRecord(BitmapSource image)
    {
        try
        {
            // Ensure image is frozen
            if (!image.IsFrozen)
            {
                image = image.Clone();
                image.Freeze();
            }

            var dir = AppSettings.Instance.HistoryDirectory;
            var clipsDir = Path.Combine(dir, "clips");
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(clipsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var clipPath = Path.Combine(clipsDir, $"clip_{timestamp}.png");

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var fs = File.Create(clipPath))
                encoder.Save(fs);

            AddEntry(image, clipPath, RecordType.Clipboard);
        }
        catch { }
    }

    private static void AddEntry(BitmapSource image, string filePath, RecordType type)
    {
        try
        {
            // Ensure image is frozen
            if (!image.IsFrozen)
            {
                image = image.Clone();
                image.Freeze();
            }

            string thumbPath = "";

            // Save thumbnail to disk if history saving is enabled
            if (AppSettings.Instance.SaveHistory)
            {
                var dir = AppSettings.Instance.HistoryDirectory;
                var thumbDir = Path.Combine(dir, "thumbnails");
                Directory.CreateDirectory(dir);
                Directory.CreateDirectory(thumbDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                thumbPath = Path.Combine(thumbDir, $"thumb_{timestamp}.png");

                double scale = Math.Min(200.0 / image.PixelWidth, 1.0);
                var thumb = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
                thumb.Freeze();

                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(thumb));
                using (var fs = File.Create(thumbPath))
                    enc.Save(fs);
            }

            // Always track in memory
            var record = new ScreenshotRecord
            {
                FilePath = filePath,
                ThumbnailPath = thumbPath,
                CapturedAt = DateTime.Now,
                Width = image.PixelWidth,
                Height = image.PixelHeight,
                Type = type
            };

            _records.Insert(0, record);

            while (_records.Count > AppSettings.Instance.MaxHistoryItems)
            {
                var old = _records[^1];
                if (!string.IsNullOrEmpty(old.ThumbnailPath))
                    try { File.Delete(old.ThumbnailPath); } catch { }
                _records.RemoveAt(_records.Count - 1);
            }

            if (AppSettings.Instance.SaveHistory)
                Save();
        }
        catch { }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Instance.HistoryDirectory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(_records, options));
        }
        catch { }
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

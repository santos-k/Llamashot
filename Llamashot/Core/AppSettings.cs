using System.IO;
using System.Text.Json;

namespace Llamashot.Core;

public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Llamashot");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Instance { get; private set; } = new();

    // Hotkeys
    public string CaptureHotkey { get; set; } = "PrintScreen";
    public string FullscreenSaveHotkey { get; set; } = "Shift+PrintScreen";
    public string FullscreenClipboardHotkey { get; set; } = "Ctrl+PrintScreen";

    // Save settings
    public string DefaultSaveFormat { get; set; } = "PNG";
    public int JpegQuality { get; set; } = 90;
    public string LastSaveDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

    // Behavior
    public bool AutoStart { get; set; } = false;
    public bool CaptureCursor { get; set; } = false;
    public bool ShowNotifications { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;

    // Drawing defaults
    public string DefaultColor { get; set; } = "#FF0000";
    public int DefaultThickness { get; set; } = 2;

    // History
    public bool SaveHistory { get; set; } = true;
    public string HistoryDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Llamashot");
    public int MaxHistoryItems { get; set; } = 100;

    public static void Load()
    {
        if (!File.Exists(SettingsPath))
        {
            Instance = new AppSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            Instance = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            Instance = new AppSettings();
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(Instance, options);
        File.WriteAllText(SettingsPath, json);
    }
}

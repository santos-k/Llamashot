using System.IO;
using System.Text.Json;

namespace Llamashot.Core;

public class AppSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Llamashot");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Instance { get; private set; } = new();

    // Global hotkeys (system-wide, registered via RegisterHotKey)
    public string CaptureHotkey { get; set; } = "PrintScreen";
    public string FullscreenSaveHotkey { get; set; } = "Shift+PrintScreen";
    public string FullscreenClipboardHotkey { get; set; } = "Ctrl+PrintScreen";

    // Tool shortcuts (active during overlay)
    public string ShortcutSave { get; set; } = "Ctrl+S";
    public string ShortcutCopy { get; set; } = "Ctrl+C";
    public string ShortcutUndo { get; set; } = "Ctrl+Z";
    public string ShortcutRedo { get; set; } = "Ctrl+Y";
    public string ShortcutPen { get; set; } = "P";
    public string ShortcutLine { get; set; } = "L";
    public string ShortcutArrow { get; set; } = "A";
    public string ShortcutRectangle { get; set; } = "R";
    public string ShortcutEllipse { get; set; } = "E";
    public string ShortcutText { get; set; } = "T";
    public string ShortcutMarker { get; set; } = "M";
    public string ShortcutBlur { get; set; } = "B";
    public string ShortcutEraser { get; set; } = "X";
    public string ShortcutObjectEraser { get; set; } = "G";
    public string ShortcutMove { get; set; } = "V";
    public string ShortcutCheck { get; set; } = "K";
    public string ShortcutCross { get; set; } = "D";
    public string ShortcutColor { get; set; } = "C";
    public string ShortcutThickness { get; set; } = "W";
    public string ShortcutHistory { get; set; } = "H";
    public string ShortcutRecord { get; set; } = "Ctrl+R";
    public string ShortcutOcr { get; set; } = "O";
    public string ShortcutPin { get; set; } = "F";

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
    public int DefaultThickness { get; set; } = 3;

    // Updates
    public bool AutoCheckUpdates { get; set; } = true;

    // Recording
    public bool RecordAudio { get; set; } = false;

    // History
    public bool SaveHistory { get; set; } = true;
    public string HistoryDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Llamashot");
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
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Instance, options));
    }
}

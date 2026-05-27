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
    public string HistoryHotkey { get; set; } = "Alt+PrintScreen";

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
    public string ShortcutMarker { get; set; } = "H";
    public string ShortcutBlur { get; set; } = "B";
    public string ShortcutEraser { get; set; } = "X";
    public string ShortcutObjectEraser { get; set; } = "G";
    public string ShortcutMove { get; set; } = "V";
    public string ShortcutCheck { get; set; } = "K";
    public string ShortcutCross { get; set; } = "D";
    public string ShortcutEmoji { get; set; } = "J";
    public string ShortcutColor { get; set; } = "C";
    public string ShortcutThickness { get; set; } = "W";
    public string ShortcutHistory { get; set; } = "Ctrl+H";
    public string ShortcutRecord { get; set; } = "Ctrl+R";
    public string ShortcutOcr { get; set; } = "O";
    public string ShortcutPin { get; set; } = "F";

    // Recording-specific shortcuts (shared section with tool shortcuts)
    public string ShortcutRecMic { get; set; } = "N";
    public string ShortcutRecSystemAudio { get; set; } = "S";
    public string ShortcutRecPause { get; set; } = "Space";
    public string ShortcutRecStop { get; set; } = "Q";
    public string ShortcutRecClearAll { get; set; } = "Ctrl+Delete";

    // Snipping toolbar shortcuts
    public string ShortcutModeScreenshot { get; set; } = "D1";
    public string ShortcutModeVideo { get; set; } = "D2";
    public string ShortcutModeGif { get; set; } = "D3";
    public string ShortcutModeScroll { get; set; } = "D4";
    public string ShortcutModeOcr { get; set; } = "D5";
    public string ShortcutToolbarRegion { get; set; } = "R";
    public string ShortcutToolbarWindow { get; set; } = "W";
    public string ShortcutToolbarFullscreen { get; set; } = "F";

    // Scroll capture
    public bool ScrollAutoMode { get; set; } = true;

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
    public string DefaultColor { get; set; } = "#FFFF00";
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

    // Quick Preview
    public bool QuickPreviewEnabled { get; set; } = true;

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

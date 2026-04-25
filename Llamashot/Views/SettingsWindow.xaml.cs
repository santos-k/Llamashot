using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Llamashot.Core;

namespace Llamashot.Views;

public partial class SettingsWindow : Window
{
    // Default hotkeys for reset
    private static readonly Dictionary<string, string> Defaults = new()
    {
        { "CaptureHotkey", "PrintScreen" },
        { "FullscreenSaveHotkey", "Shift+PrintScreen" },
        { "FullscreenClipHotkey", "Ctrl+PrintScreen" }
    };

    public SettingsWindow()
    {
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = AppSettings.Instance;

        ChkAutoStart.IsChecked = s.AutoStart;
        ChkCaptureCursor.IsChecked = s.CaptureCursor;
        ChkShowNotifications.IsChecked = s.ShowNotifications;
        ChkMinimizeToTray.IsChecked = s.MinimizeToTray;

        TxtCaptureHotkey.Text = s.CaptureHotkey;
        TxtFullscreenSaveHotkey.Text = s.FullscreenSaveHotkey;
        TxtFullscreenClipHotkey.Text = s.FullscreenClipboardHotkey;

        foreach (ComboBoxItem item in CmbFormat.Items)
        {
            if (item.Content.ToString() == s.DefaultSaveFormat)
            { CmbFormat.SelectedItem = item; break; }
        }

        SldJpegQuality.Value = s.JpegQuality;
        TxtJpegQuality.Text = s.JpegQuality.ToString();
        SldJpegQuality.ValueChanged += (_, e) =>
            TxtJpegQuality.Text = ((int)e.NewValue).ToString();

        TxtSaveDir.Text = s.LastSaveDirectory;
        ChkSaveHistory.IsChecked = s.SaveHistory;
        TxtMaxHistory.Text = s.MaxHistoryItems.ToString();
    }

    // ============ HOTKEY RECORDING ============

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
            tb.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x35));
        }
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            tb.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E));
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore lone modifier keys
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin)
            return;

        // Build combo string
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");

        // Map key name
        string keyName = key switch
        {
            Key.Snapshot => "PrintScreen",
            Key.OemPlus => "Plus",
            Key.OemMinus => "Minus",
            Key.OemTilde => "Tilde",
            Key.Back => "Backspace",
            Key.Return => "Enter",
            _ => key.ToString()
        };

        parts.Add(keyName);
        tb.Text = string.Join("+", parts);

        ValidateHotkeys();
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        if (Defaults.TryGetValue(tag, out var defaultVal))
        {
            var tb = tag switch
            {
                "CaptureHotkey" => TxtCaptureHotkey,
                "FullscreenSaveHotkey" => TxtFullscreenSaveHotkey,
                "FullscreenClipHotkey" => TxtFullscreenClipHotkey,
                _ => null
            };
            if (tb != null) tb.Text = defaultVal;
        }

        ValidateHotkeys();
    }

    private void ValidateHotkeys()
    {
        var hotkeys = new Dictionary<string, string>
        {
            { "Capture region", TxtCaptureHotkey.Text },
            { "Fullscreen save", TxtFullscreenSaveHotkey.Text },
            { "Fullscreen copy", TxtFullscreenClipHotkey.Text }
        };

        var duplicates = hotkeys
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(kv => kv.Key))
            .ToList();

        if (duplicates.Count > 0)
        {
            TxtHotkeyWarning.Text = $"Duplicate hotkey: {string.Join(", ", duplicates)}";
            TxtHotkeyWarning.Visibility = Visibility.Visible;
        }
        else
        {
            TxtHotkeyWarning.Visibility = Visibility.Collapsed;
        }

        // Highlight duplicates
        HighlightDuplicate(TxtCaptureHotkey, duplicates.Contains("Capture region"));
        HighlightDuplicate(TxtFullscreenSaveHotkey, duplicates.Contains("Fullscreen save"));
        HighlightDuplicate(TxtFullscreenClipHotkey, duplicates.Contains("Fullscreen copy"));
    }

    private void HighlightDuplicate(TextBox tb, bool isDuplicate)
    {
        if (isDuplicate)
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
        else if (tb.IsFocused)
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
        else
            tb.BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    }

    // ============ SAVE / CANCEL ============

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Check for duplicates
        ValidateHotkeys();
        if (TxtHotkeyWarning.Visibility == Visibility.Visible)
        {
            System.Windows.MessageBox.Show("Please fix duplicate hotkeys before saving.",
                "Llamashot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var s = AppSettings.Instance;

        s.AutoStart = ChkAutoStart.IsChecked ?? false;
        s.CaptureCursor = ChkCaptureCursor.IsChecked ?? false;
        s.ShowNotifications = ChkShowNotifications.IsChecked ?? true;
        s.MinimizeToTray = ChkMinimizeToTray.IsChecked ?? true;

        s.CaptureHotkey = TxtCaptureHotkey.Text;
        s.FullscreenSaveHotkey = TxtFullscreenSaveHotkey.Text;
        s.FullscreenClipboardHotkey = TxtFullscreenClipHotkey.Text;

        s.DefaultSaveFormat = (CmbFormat.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "PNG";
        s.JpegQuality = (int)SldJpegQuality.Value;
        s.LastSaveDirectory = TxtSaveDir.Text;

        s.SaveHistory = ChkSaveHistory.IsChecked ?? true;
        if (int.TryParse(TxtMaxHistory.Text, out int max) && max > 0)
            s.MaxHistoryItems = max;

        SetAutoStart(s.AutoStart);
        AppSettings.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void BrowseDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = TxtSaveDir.Text,
            Description = "Select save directory"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtSaveDir.Text = dialog.SelectedPath;
    }

    private static void SetAutoStart(bool enable)
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "Llamashot";

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, true);
        if (key == null) return;

        if (enable)
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                key.SetValue(valueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(valueName, false);
        }
    }
}

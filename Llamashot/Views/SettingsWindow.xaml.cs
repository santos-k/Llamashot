using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Llamashot.Core;

namespace Llamashot.Views;

public partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, string> GlobalDefaults = new()
    {
        { "CaptureHotkey", "PrintScreen" },
        { "FullscreenSaveHotkey", "Shift+PrintScreen" },
        { "FullscreenClipHotkey", "Ctrl+PrintScreen" }
    };

    private static readonly (string Key, string Label, string Default)[] ToolShortcuts =
    {
        ("ShortcutSave", "Save", "Ctrl+S"),
        ("ShortcutCopy", "Copy", "Ctrl+C"),
        ("ShortcutUndo", "Undo", "Ctrl+Z"),
        ("ShortcutRedo", "Redo", "Ctrl+Y"),
        ("ShortcutPen", "Pencil", "P"),
        ("ShortcutLine", "Line", "L"),
        ("ShortcutArrow", "Arrow", "A"),
        ("ShortcutRectangle", "Rectangle", "R"),
        ("ShortcutEllipse", "Ellipse", "E"),
        ("ShortcutText", "Text", "T"),
        ("ShortcutMarker", "Marker", "M"),
        ("ShortcutBlur", "Blur", "B"),
        ("ShortcutEraser", "Undo last", "X"),
        ("ShortcutObjectEraser", "Eraser", "G"),
        ("ShortcutMove", "Move", "V"),
        ("ShortcutCheck", "Check mark", "K"),
        ("ShortcutCross", "Cross mark", "D"),
        ("ShortcutColor", "Color", "C"),
        ("ShortcutThickness", "Thickness", "W"),
        ("ShortcutHistory", "History", "H"),
        ("ShortcutRecord", "Record", "Ctrl+R"),
        ("ShortcutOcr", "OCR", "O"),
        ("ShortcutPin", "Pin", "F"),
    };

    private readonly Dictionary<string, TextBox> _toolShortcutBoxes = new();

    public SettingsWindow()
    {
        InitializeComponent();
        BuildToolShortcutFields();
        LoadSettings();
    }

    private void BuildToolShortcutFields()
    {
        foreach (var (key, label, def) in ToolShortcuts)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var lbl = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);

            var tb = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1E)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEE)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(1),
                Height = 28,
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 12,
                IsReadOnly = true,
                Cursor = Cursors.Hand
            };
            tb.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
            tb.GotFocus += HotkeyBox_GotFocus;
            tb.LostFocus += HotkeyBox_LostFocus;
            Grid.SetColumn(tb, 1);

            var resetBtn = new Button
            {
                Content = "Reset",
                Width = 44,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Cursor = Cursors.Hand,
                Tag = key
            };
            resetBtn.Click += (s, e) =>
            {
                tb.Text = def;
                ValidateAllShortcuts();
            };
            Grid.SetColumn(resetBtn, 2);

            grid.Children.Add(lbl);
            grid.Children.Add(tb);
            grid.Children.Add(resetBtn);

            ToolShortcutsPanel.Children.Add(grid);
            _toolShortcutBoxes[key] = tb;
        }
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

        // Load tool shortcuts
        _toolShortcutBoxes["ShortcutSave"].Text = s.ShortcutSave;
        _toolShortcutBoxes["ShortcutCopy"].Text = s.ShortcutCopy;
        _toolShortcutBoxes["ShortcutUndo"].Text = s.ShortcutUndo;
        _toolShortcutBoxes["ShortcutRedo"].Text = s.ShortcutRedo;
        _toolShortcutBoxes["ShortcutPen"].Text = s.ShortcutPen;
        _toolShortcutBoxes["ShortcutLine"].Text = s.ShortcutLine;
        _toolShortcutBoxes["ShortcutArrow"].Text = s.ShortcutArrow;
        _toolShortcutBoxes["ShortcutRectangle"].Text = s.ShortcutRectangle;
        _toolShortcutBoxes["ShortcutEllipse"].Text = s.ShortcutEllipse;
        _toolShortcutBoxes["ShortcutText"].Text = s.ShortcutText;
        _toolShortcutBoxes["ShortcutMarker"].Text = s.ShortcutMarker;
        _toolShortcutBoxes["ShortcutBlur"].Text = s.ShortcutBlur;
        _toolShortcutBoxes["ShortcutEraser"].Text = s.ShortcutEraser;
        _toolShortcutBoxes["ShortcutObjectEraser"].Text = s.ShortcutObjectEraser;
        _toolShortcutBoxes["ShortcutMove"].Text = s.ShortcutMove;
        _toolShortcutBoxes["ShortcutCheck"].Text = s.ShortcutCheck;
        _toolShortcutBoxes["ShortcutCross"].Text = s.ShortcutCross;
        _toolShortcutBoxes["ShortcutColor"].Text = s.ShortcutColor;
        _toolShortcutBoxes["ShortcutThickness"].Text = s.ShortcutThickness;
        _toolShortcutBoxes["ShortcutHistory"].Text = s.ShortcutHistory;
        _toolShortcutBoxes["ShortcutRecord"].Text = s.ShortcutRecord;
        _toolShortcutBoxes["ShortcutOcr"].Text = s.ShortcutOcr;
        _toolShortcutBoxes["ShortcutPin"].Text = s.ShortcutPin;

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
        ChkRecordAudio.IsChecked = s.RecordAudio;
        ChkSaveHistory.IsChecked = s.SaveHistory;
        TxtHistoryDir.Text = s.HistoryDirectory;
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
        ValidateAllShortcuts();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin)
            return;

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");

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
        ValidateAllShortcuts();
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string tag) return;

        if (GlobalDefaults.TryGetValue(tag, out var defaultVal))
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
        ValidateAllShortcuts();
    }

    private void ValidateAllShortcuts()
    {
        // Collect all shortcut values with labels
        var all = new Dictionary<string, string>
        {
            { "Capture region", TxtCaptureHotkey.Text },
            { "Fullscreen save", TxtFullscreenSaveHotkey.Text },
            { "Fullscreen copy", TxtFullscreenClipHotkey.Text }
        };

        foreach (var (key, label, _) in ToolShortcuts)
        {
            if (_toolShortcutBoxes.TryGetValue(key, out var tb))
                all[label] = tb.Text;
        }

        var duplicates = all
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(kv => kv.Key))
            .ToHashSet();

        if (duplicates.Count > 0)
        {
            TxtHotkeyWarning.Text = $"Duplicate: {string.Join(", ", duplicates)}";
            TxtHotkeyWarning.Visibility = Visibility.Visible;
        }
        else
        {
            TxtHotkeyWarning.Visibility = Visibility.Collapsed;
        }

        // Highlight global hotkey duplicates
        SetBorder(TxtCaptureHotkey, duplicates.Contains("Capture region"));
        SetBorder(TxtFullscreenSaveHotkey, duplicates.Contains("Fullscreen save"));
        SetBorder(TxtFullscreenClipHotkey, duplicates.Contains("Fullscreen copy"));

        // Highlight tool shortcut duplicates
        foreach (var (key, label, _) in ToolShortcuts)
        {
            if (_toolShortcutBoxes.TryGetValue(key, out var tb))
                SetBorder(tb, duplicates.Contains(label));
        }
    }

    private void SetBorder(TextBox tb, bool isDuplicate)
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
        ValidateAllShortcuts();
        if (TxtHotkeyWarning.Visibility == Visibility.Visible)
        {
            System.Windows.MessageBox.Show("Please fix duplicate shortcuts before saving.",
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

        // Save tool shortcuts
        s.ShortcutSave = _toolShortcutBoxes["ShortcutSave"].Text;
        s.ShortcutCopy = _toolShortcutBoxes["ShortcutCopy"].Text;
        s.ShortcutUndo = _toolShortcutBoxes["ShortcutUndo"].Text;
        s.ShortcutRedo = _toolShortcutBoxes["ShortcutRedo"].Text;
        s.ShortcutPen = _toolShortcutBoxes["ShortcutPen"].Text;
        s.ShortcutLine = _toolShortcutBoxes["ShortcutLine"].Text;
        s.ShortcutArrow = _toolShortcutBoxes["ShortcutArrow"].Text;
        s.ShortcutRectangle = _toolShortcutBoxes["ShortcutRectangle"].Text;
        s.ShortcutEllipse = _toolShortcutBoxes["ShortcutEllipse"].Text;
        s.ShortcutText = _toolShortcutBoxes["ShortcutText"].Text;
        s.ShortcutMarker = _toolShortcutBoxes["ShortcutMarker"].Text;
        s.ShortcutBlur = _toolShortcutBoxes["ShortcutBlur"].Text;
        s.ShortcutEraser = _toolShortcutBoxes["ShortcutEraser"].Text;
        s.ShortcutObjectEraser = _toolShortcutBoxes["ShortcutObjectEraser"].Text;
        s.ShortcutMove = _toolShortcutBoxes["ShortcutMove"].Text;
        s.ShortcutCheck = _toolShortcutBoxes["ShortcutCheck"].Text;
        s.ShortcutCross = _toolShortcutBoxes["ShortcutCross"].Text;
        s.ShortcutColor = _toolShortcutBoxes["ShortcutColor"].Text;
        s.ShortcutThickness = _toolShortcutBoxes["ShortcutThickness"].Text;
        s.ShortcutHistory = _toolShortcutBoxes["ShortcutHistory"].Text;
        s.ShortcutRecord = _toolShortcutBoxes["ShortcutRecord"].Text;
        s.ShortcutOcr = _toolShortcutBoxes["ShortcutOcr"].Text;
        s.ShortcutPin = _toolShortcutBoxes["ShortcutPin"].Text;

        s.DefaultSaveFormat = (CmbFormat.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "PNG";
        s.JpegQuality = (int)SldJpegQuality.Value;
        s.LastSaveDirectory = TxtSaveDir.Text;

        s.RecordAudio = ChkRecordAudio.IsChecked ?? false;
        s.SaveHistory = ChkSaveHistory.IsChecked ?? true;
        s.HistoryDirectory = TxtHistoryDir.Text;
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

    private void BrowseHistoryDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = TxtHistoryDir.Text,
            Description = "Select history folder"
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtHistoryDir.Text = dialog.SelectedPath;
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

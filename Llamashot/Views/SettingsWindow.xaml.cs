using System.Windows;
using System.Windows.Controls;
using Llamashot.Core;
using Microsoft.Win32;

namespace Llamashot.Views;

public partial class SettingsWindow : Window
{
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

        // Format
        foreach (ComboBoxItem item in CmbFormat.Items)
        {
            if (item.Content.ToString() == s.DefaultSaveFormat)
            {
                CmbFormat.SelectedItem = item;
                break;
            }
        }

        SldJpegQuality.Value = s.JpegQuality;
        TxtJpegQuality.Text = s.JpegQuality.ToString();
        SldJpegQuality.ValueChanged += (_, e) =>
            TxtJpegQuality.Text = ((int)e.NewValue).ToString();

        TxtSaveDir.Text = s.LastSaveDirectory;

        ChkSaveHistory.IsChecked = s.SaveHistory;
        TxtMaxHistory.Text = s.MaxHistoryItems.ToString();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
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

        // Handle auto-start
        SetAutoStart(s.AutoStart);

        AppSettings.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

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

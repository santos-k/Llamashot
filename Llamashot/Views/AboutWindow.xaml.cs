using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Llamashot.Core;

namespace Llamashot.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (s, e) => ThemeManager.ThemeChanged -= OnThemeChanged;

        // If a background check already found an update, show it immediately
        if (UpdateChecker.LatestUpdate != null)
        {
            TxtUpdateHeadline.Text = $"Update available — v{UpdateChecker.LatestUpdate.Version}";
            TxtUpdateHeadline.Foreground = AmberBrush;
            TxtUpdateStatus.Text = "A newer version is ready to install.";
            TxtUpdateStatus.Foreground = (Brush)FindResource("SuccessBrush");
            TxtUpdateStatus.Visibility = Visibility.Visible;
            BtnUpdate.Content = $"Update now to v{UpdateChecker.LatestUpdate.Version}";
        }
    }

    // Update-available highlight — kept as a fixed amber accent across both themes.
    private static readonly SolidColorBrush AmberBrush =
        new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x6B));

    // Re-resolve the themed status brushes when the user toggles light/dark live.
    private void OnThemeChanged()
    {
        if (TxtUpdateHeadline.Foreground is SolidColorBrush hb && hb.Color != AmberBrush.Color)
            TxtUpdateHeadline.Foreground = (Brush)FindResource("TextPrimaryBrush");

        // The status line is only meaningful while visible; repaint good/error tints.
        if (TxtUpdateStatus.Visibility == Visibility.Visible)
        {
            if (_statusIsError)
                TxtUpdateStatus.Foreground = (Brush)FindResource("DangerBrush");
            else
                TxtUpdateStatus.Foreground = (Brush)FindResource("SuccessBrush");
        }
    }

    private bool _statusIsError;

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppSettings.WebsiteUrl) { UseShellExecute = true }); } catch { }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnUpdate.IsEnabled = false;

        try
        {
            var update = UpdateChecker.LatestUpdate;

            if (update == null)
            {
                BtnUpdate.Content = "Checking...";
                TxtUpdateStatus.Visibility = Visibility.Collapsed;
                update = await UpdateChecker.CheckForUpdateAsync();
            }

            if (update == null)
            {
                TxtUpdateHeadline.Text = "You're on the latest version";
                TxtUpdateHeadline.Foreground = (Brush)FindResource("TextPrimaryBrush");
                TxtUpdateStatus.Text = "✓ Up to date";
                TxtUpdateStatus.Foreground = (Brush)FindResource("SuccessBrush");
                _statusIsError = false;
                TxtUpdateStatus.Visibility = Visibility.Visible;
                BtnUpdate.Content = "Check for Update";
                BtnUpdate.IsEnabled = true;
                return;
            }

            TxtUpdateHeadline.Text = $"Update available — v{update.Version}";
            TxtUpdateHeadline.Foreground = AmberBrush;

            // Download silently with progress
            BtnUpdate.Content = $"Downloading v{update.Version}...";
            PrgUpdate.Value = 0;
            PrgUpdate.IsIndeterminate = false;
            PrgUpdate.Visibility = Visibility.Visible;
            TxtUpdateStatus.Visibility = Visibility.Collapsed;

            var progress = new Progress<double>(p => PrgUpdate.Value = p);
            var installerPath = await UpdateChecker.DownloadUpdateAsync(update, progress);

            if (installerPath == null)
            {
                ShowError("Download failed");
                return;
            }

            // Launch silent install and restart
            BtnUpdate.Content = "Installing...";
            PrgUpdate.IsIndeterminate = true;

            LaunchSilentInstall(installerPath);
        }
        catch
        {
            ShowError("Update failed. Try again later.");
        }
    }

    private void ShowError(string message)
    {
        TxtUpdateStatus.Text = message;
        TxtUpdateStatus.Foreground = (Brush)FindResource("DangerBrush");
        _statusIsError = true;
        TxtUpdateStatus.Visibility = Visibility.Visible;
        PrgUpdate.Visibility = Visibility.Collapsed;
        BtnUpdate.Content = "Check for Update";
        BtnUpdate.IsEnabled = true;
    }

    private static void LaunchSilentInstall(string installerPath)
    {
        // Prefer the standard install location over current process path (handles debug builds)
        var installExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Llamashot", "Llamashot.exe");
        var appPath = File.Exists(installExe) ? installExe : (Environment.ProcessPath ?? installExe);

        var batchDir = Path.GetDirectoryName(installerPath)!;
        var batchPath = Path.Combine(batchDir, "update.cmd");

        File.WriteAllText(batchPath,
            $"@echo off\r\n" +
            $"timeout /t 3 /nobreak >nul\r\n" +
            $"\"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS\r\n" +
            $"timeout /t 2 /nobreak >nul\r\n" +
            $"start \"\" \"{appPath}\"\r\n" +
            $"del \"%~f0\"\r\n");

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batchPath}\"",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false
        });

        Application.Current.Shutdown();
    }
}

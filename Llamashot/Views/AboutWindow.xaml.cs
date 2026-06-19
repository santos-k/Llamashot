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

        // If a background check already found an update, show it immediately
        if (UpdateChecker.LatestUpdate != null)
        {
            TxtUpdateHeadline.Text = $"Update available — v{UpdateChecker.LatestUpdate.Version}";
            TxtUpdateHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x6B));
            TxtUpdateStatus.Text = "A newer version is ready to install.";
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xE6, 0xB0));
            TxtUpdateStatus.Visibility = Visibility.Visible;
            BtnUpdate.Content = $"Update now to v{UpdateChecker.LatestUpdate.Version}";
        }
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
                TxtUpdateHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xEA, 0xEA, 0xF0));
                TxtUpdateStatus.Text = "✓ Up to date";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x5B, 0xD0, 0xA4));
                TxtUpdateStatus.Visibility = Visibility.Visible;
                BtnUpdate.Content = "Check for Update";
                BtnUpdate.IsEnabled = true;
                return;
            }

            TxtUpdateHeadline.Text = $"Update available — v{update.Version}";
            TxtUpdateHeadline.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x6B));

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
        TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
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

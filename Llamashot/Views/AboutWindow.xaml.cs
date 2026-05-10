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
            TxtUpdateStatus.Text = $"Version {UpdateChecker.LatestUpdate.Version} available";
            TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
            TxtUpdateStatus.Visibility = Visibility.Visible;
            BtnUpdate.Content = $"Update to v{UpdateChecker.LatestUpdate.Version}";
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
                TxtUpdateStatus.Text = "You're up to date";
                TxtUpdateStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
                TxtUpdateStatus.Visibility = Visibility.Visible;
                BtnUpdate.Content = "Check for Update";
                BtnUpdate.IsEnabled = true;
                return;
            }

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
        var appPath = Environment.ProcessPath ?? "";
        var batchDir = Path.GetDirectoryName(installerPath)!;
        var batchPath = Path.Combine(batchDir, "update.cmd");

        File.WriteAllText(batchPath,
            $"@echo off\r\n" +
            $"timeout /t 2 /nobreak >nul\r\n" +
            $"\"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS\r\n" +
            $"timeout /t 1 /nobreak >nul\r\n" +
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

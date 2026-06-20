using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Llamashot.Core;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;
using WpfButton = System.Windows.Controls.Button;

namespace Llamashot.Views;

/// <summary>
/// Universal link downloader: fetches any direct file (image, document, archive, installer, …) over
/// HTTP with progress, and routes media-site links (YouTube, Instagram, Facebook, X/Twitter, TikTok, …)
/// through yt-dlp. Multiple links can be queued, one per line. Backed by <see cref="FileToolsService"/>.
/// </summary>
public partial class LinkDownloaderWindow : Window
{
    private string _outputDir;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public LinkDownloaderWindow()
    {
        InitializeComponent();
        _outputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try { Directory.CreateDirectory(_outputDir); } catch { _outputDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        TxtOutDir.Text = _outputDir;
    }

    private static IEnumerable<string> ParseUrls(string text) => text
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Trim())
        .Where(l => l.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || l.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    private void Urls_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var urls = ParseUrls(TxtUrls.Text).ToList();
        if (urls.Count == 0) { TxtKind.Text = ""; return; }
        int direct = urls.Count(u => FileToolsService.ClassifyUrl(u) == FileToolsService.LinkKind.DirectFile);
        int media = urls.Count - direct;
        TxtKind.Text = $"{urls.Count} link(s) · {direct} file, {media} media";
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string clip = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clip)) return;
            TxtUrls.Text = string.IsNullOrWhiteSpace(TxtUrls.Text) ? clip : TxtUrls.Text.TrimEnd() + "\n" + clip;
            TxtUrls.CaretIndex = TxtUrls.Text.Length;
        }
        catch { }
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => TxtUrls.Clear();

    private void ChangeDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose download folder", InitialDirectory = _outputDir };
        if (dlg.ShowDialog(this) == true)
        {
            _outputDir = dlg.FolderName;
            TxtOutDir.Text = _outputDir;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var urls = ParseUrls(TxtUrls.Text).ToList();
        if (urls.Count == 0)
        {
            ConfirmDialog.Alert(this, "No Links", "Paste one or more http(s) links (one per line).", ConfirmDialog.AlertKind.Info);
            return;
        }

        _busy = true;
        _cts = new CancellationTokenSource();
        SetBusyUi(true);

        int ok = 0, fail = 0;
        for (int i = 0; i < urls.Count; i++)
        {
            if (_cts.IsCancellationRequested) break;
            string url = urls[i];
            string head = urls.Count > 1 ? $"[{i + 1}/{urls.Count}] " : "";
            Prg.Value = 0;
            var prog = new Progress<(int percent, string status)>(p =>
            {
                Prg.Value = p.percent;
                TxtStatus.Text = $"{head}{Trim(url)} — {p.status}";
            });

            try
            {
                var kind = FileToolsService.ClassifyUrl(url);
                if (kind == FileToolsService.LinkKind.Media && !FileToolsService.IsYtDlpAvailable())
                    throw new Exception("yt-dlp is required for media sites. Install it with:  winget install yt-dlp.yt-dlp");

                TxtStatus.Text = $"{head}{Trim(url)} — starting…";
                string? path = kind == FileToolsService.LinkKind.DirectFile
                    ? await FileToolsService.DownloadDirectFileAsync(url, _outputDir, prog, _cts.Token)
                    : await FileToolsService.DownloadMediaAsync(url, _outputDir, ChkAudio.IsChecked == true, prog);

                if (path != null && File.Exists(path)) { AddDone(path, null); ok++; }
                else { AddDone(url, "Saved to the download folder."); ok++; }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { AddDone(url, "Failed: " + ex.Message); fail++; }
        }

        Prg.Value = 0;
        TxtStatus.Text = _cts.IsCancellationRequested
            ? $"Cancelled — {ok} done, {fail} failed."
            : $"Finished — {ok} downloaded{(fail > 0 ? $", {fail} failed" : "")}.";
        SetBusyUi(false);
        _busy = false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetBusyUi(bool busy)
    {
        BtnDownload.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        BtnCancel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Prg.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        TxtUrls.IsEnabled = !busy; BtnPaste.IsEnabled = !busy; BtnClear.IsEnabled = !busy;
        BtnChangeDir.IsEnabled = !busy; ChkAudio.IsEnabled = !busy;
    }

    private static string Trim(string url) => url.Length > 60 ? url[..57] + "…" : url;

    /// <summary>Adds a row to the Downloads list: filename + Open / Folder, or an error message.</summary>
    private void AddDone(string pathOrUrl, string? message)
    {
        bool isFile = message == null || File.Exists(pathOrUrl);
        var row = new System.Windows.Controls.Border
        {
            Background = (Brush)FindResource("SurfaceAltBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
        };
        var grid = new System.Windows.Controls.Grid();
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var text = new System.Windows.Controls.TextBlock
        {
            Text = isFile ? Path.GetFileName(pathOrUrl) : (message ?? pathOrUrl),
            Foreground = (Brush)FindResource(isFile ? "TextSecondaryBrush" : "DangerBrush"),
            FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = pathOrUrl,
        };
        System.Windows.Controls.Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        if (isFile && File.Exists(pathOrUrl))
        {
            var btns = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            System.Windows.Controls.Grid.SetColumn(btns, 1);
            btns.Children.Add(MakeMiniButton("Open", (_, _) => DocumentScanWindow.OpenSaved(pathOrUrl)));
            btns.Children.Add(MakeMiniButton("Folder", (_, _) =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{pathOrUrl}\""); } catch { }
            }));
            grid.Children.Add(btns);
        }
        row.Child = grid;
        DoneList.Children.Insert(0, row);
    }

    private WpfButton MakeMiniButton(string label, RoutedEventHandler onClick)
    {
        var b = new WpfButton
        {
            Content = label, Style = (Style)FindResource("Tool"),
            Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(6, 0, 0, 0), FontSize = 11,
        };
        b.Click += onClick;
        return b;
    }
}

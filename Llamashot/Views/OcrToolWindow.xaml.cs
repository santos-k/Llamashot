using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Llamashot.Core;
using Llamashot.Core.Ocr;
using Microsoft.Win32;

namespace Llamashot.Views;

public partial class OcrToolWindow : Window
{
    private sealed record EngineItem(string Id, string Name);

    private string? _filePath;
    private string? _pdfPassword;
    private OcrDocument? _doc;
    private CancellationTokenSource? _cts;

    public OcrToolWindow()
    {
        InitializeComponent();
        PopulateEngines();
        UpdateEmptyState();
    }

    // ------------------------------------------------------------------ setup

    private void PopulateEngines()
    {
        var available = OcrEngines.Available;
        CboEngine.ItemsSource = available.Select(e => new EngineItem(e.Id, e.DisplayName)).ToList();
        if (CboEngine.Items.Count > 0)
            CboEngine.SelectedIndex = 0; // most-accurate first (Tesseract when present)
    }

    private void Engine_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        string? id = (CboEngine.SelectedItem as EngineItem)?.Id;
        var engine = OcrEngines.ById(id);
        var langs = engine?.GetLanguages() ?? new List<OcrLanguageOption>();
        CboLang.ItemsSource = langs;
        if (langs.Count > 0)
        {
            int idx = langs.ToList().FindIndex(l =>
                l.Code.StartsWith("eng", StringComparison.OrdinalIgnoreCase) ||
                l.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            CboLang.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    // ------------------------------------------------------------------ open

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open an image or PDF",
            Filter = "Images & PDF|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp;*.pdf|" +
                     "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif;*.webp|PDF|*.pdf|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        await OpenFileAsync(dlg.FileName);
    }

    public async Task OpenFileAsync(string path)
    {
        if (!OcrService.IsSupportedInput(path))
        {
            MessageBox.Show(this, "Please choose an image or PDF file.", "Unsupported file",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _filePath = path;
        _pdfPassword = null;
        _doc = null;
        ResetResults();

        try
        {
            if (OcrService.IsPdf(path))
            {
                if (await FileToolsService.IsPdfEncryptedAsync(path))
                {
                    string? pw = await PasswordDialog.UnlockAsync(this, path);
                    if (pw == null) { _filePath = null; UpdateEmptyState(); return; }
                    _pdfPassword = pw;
                }
                Img.Source = await RenderPdfFirstPageAsync(path, _pdfPassword);
                int pages = await FileToolsService.GetPdfPageCountAsync(path, _pdfPassword);
                TxtSrcInfo.Text = $"{Path.GetFileName(path)}  ·  {pages} page{(pages == 1 ? "" : "s")}  ·  PDF";
            }
            else
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                Img.Source = bmp;
                TxtSrcInfo.Text = $"{Path.GetFileName(path)}  ·  {bmp.PixelWidth}×{bmp.PixelHeight}";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't open the file:\n" + ex.Message, "Open failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _filePath = null;
        }

        UpdateEmptyState();
    }

    private static async Task<BitmapSource> RenderPdfFirstPageAsync(string pdfPath, string? password)
    {
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdfPath);
        var pdf = string.IsNullOrEmpty(password)
            ? await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file)
            : await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file, password);
        using var page = pdf.GetPage(0);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions
        {
            DestinationWidth = (uint)(page.Size.Width * 130 / 72)
        });
        stream.Seek(0);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = stream.AsStreamForRead();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    // ------------------------------------------------------------------ run

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (_filePath == null) { Open_Click(sender, e); return; }
        await RunAsync();
    }

    public async Task RunAsync(bool autoSavePdf = true)
    {
        if (_filePath == null) return;

        string? engineId = (CboEngine.SelectedItem as EngineItem)?.Id;
        string lang = (CboLang.SelectedItem as OcrLanguageOption)?.Code ?? "eng";

        _cts = new CancellationTokenSource();
        SetBusy(true, "Running OCR…");
        try
        {
            var opts = new OcrOptions
            {
                EngineId = engineId,
                LangCode = lang,
                PdfPassword = _pdfPassword,
                KeepPageImages = true,
            };
            var progress = new Progress<int>(p => PrgBusy.Value = p);
            _doc = await OcrService.RecognizeAsync(_filePath, opts, progress, _cts.Token);
            ShowResults(_doc);

            // The "Create searchable PDF" intent: prompt to save right after a successful run.
            if (autoSavePdf && ChkPdf.IsChecked == true && _doc.Pages.Any(p => p.Error == null))
                await SaveSearchablePdfAsync();
        }
        catch (OperationCanceledException)
        {
            SetStatus("OCR cancelled.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "OCR failed:\n" + ex.Message, "OCR failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ShowResults(OcrDocument doc)
    {
        int wordCount = doc.Pages.Sum(p => p.Lines.Sum(l => l.Words.Count));
        TxtResultInfo.Text = $"{doc.Pages.Count} page{(doc.Pages.Count == 1 ? "" : "s")} · {wordCount} words · {doc.EngineName}";

        if (doc.Pages.Count == 1)
        {
            var p = doc.Pages[0];
            TxtResult.Text = p.Error != null ? $"[Page could not be read: {p.Error}]" : p.Text;
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < doc.Pages.Count; i++)
            {
                var p = doc.Pages[i];
                sb.Append("──────── Page ").Append(i + 1).Append(" ────────\n\n");
                sb.Append(p.Error != null ? $"[Page could not be read: {p.Error}]" : p.Text);
                sb.Append("\n\n");
            }
            TxtResult.Text = sb.ToString().TrimEnd();
        }

        TxtResultHint.Visibility = Visibility.Collapsed;
        bool hasText = !string.IsNullOrWhiteSpace(TxtResult.Text);
        BtnCopy.IsEnabled = hasText;
        BtnSaveTxt.IsEnabled = hasText;
        BtnSavePdf.IsEnabled = doc.Pages.Any(p => p.Error == null);
    }

    // ------------------------------------------------------------------ outputs

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_doc != null && !string.IsNullOrEmpty(_doc.CombinedText))
        {
            Clipboard.SetText(_doc.CombinedText);
            SetStatus("Copied text to clipboard.");
        }
    }

    private void SaveTxt_Click(object sender, RoutedEventArgs e)
    {
        if (_doc == null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Save extracted text",
            Filter = "Text file|*.txt",
            FileName = Path.GetFileNameWithoutExtension(_filePath) + ".txt",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, _doc.CombinedText);
            SetStatus("Saved " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't save:\n" + ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e) => await SaveSearchablePdfAsync();

    private async Task SaveSearchablePdfAsync()
    {
        if (_doc == null || _doc.PageImages.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Title = "Save searchable PDF",
            Filter = "PDF file|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_filePath) + "_ocr.pdf",
        };
        if (dlg.ShowDialog(this) != true) return;

        SetBusy(true, "Writing searchable PDF…");
        PrgBusy.IsIndeterminate = true;
        try
        {
            await OcrService.WriteSearchablePdfAsync(_doc, dlg.FileName);
            SetStatus("Saved searchable PDF: " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Couldn't write the PDF:\n" + ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            PrgBusy.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ------------------------------------------------------------------ helpers

    private void ResetResults()
    {
        TxtResult.Text = "";
        TxtResultInfo.Text = "Extracted text";
        TxtResultHint.Visibility = Visibility.Visible;
        BtnCopy.IsEnabled = BtnSaveTxt.IsEnabled = BtnSavePdf.IsEnabled = false;
        StatusBar.Visibility = Visibility.Collapsed;
    }

    private void UpdateEmptyState()
    {
        EmptyState.Visibility = _filePath == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        if (message != null) TxtBusy.Text = message;
        if (busy) PrgBusy.Value = 0;
        BusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BtnRun.IsEnabled = BtnOpen.IsEnabled = !busy;
    }

    private void SetStatus(string text)
    {
        TxtStatus.Text = text;
        StatusBar.Visibility = Visibility.Visible;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (BusyOverlay.Visibility == Visibility.Visible) return;
        if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.None) { Open_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Enter && _filePath != null) { Run_Click(this, new RoutedEventArgs()); e.Handled = true; }
    }
}

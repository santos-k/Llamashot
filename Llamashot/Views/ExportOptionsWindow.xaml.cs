using System.Windows;
using System.Windows.Input;

namespace Llamashot.Views;

/// <summary>
/// Modal dialog that lets the user choose Resolution, Frame Rate and Quality before exporting.
/// Show with ShowDialog(); if DialogResult is true, read Width/Height/Fps/Crf.
/// </summary>
public partial class ExportOptionsWindow : Window
{
    // ---- resolved export params (valid after OK) ----
    // Named OutWidth/OutHeight (not Width/Height) to avoid shadowing Window.Width/Height.
    public int OutWidth  { get; private set; }
    public int OutHeight { get; private set; }
    public int Fps { get; private set; }
    public int Crf { get; private set; }

    // ---- project defaults (for "Match project" items) ----
    private readonly int _projW, _projH, _projFps;

    // ---- internal selection state ----
    private int _selW, _selH, _selFps, _selCrf;

    public ExportOptionsWindow(int projectW, int projectH, int projectFps)
    {
        _projW   = projectW;
        _projH   = projectH;
        _projFps = projectFps;

        // Defaults: match project, high quality.
        _selW   = projectW;
        _selH   = projectH;
        _selFps = projectFps;
        _selCrf = 18;

        InitializeComponent();
        PopulateCombos();
    }

    private void PopulateCombos()
    {
        // Resolution
        CmbResolution.Items.Add($"Match project ({_projW} × {_projH})");
        CmbResolution.Items.Add("1920 × 1080 (1080p)");
        CmbResolution.Items.Add("1280 × 720 (720p)");
        CmbResolution.SelectedIndex = 0;

        // Frame rate
        CmbFps.Items.Add($"Match project ({_projFps} fps)");
        CmbFps.Items.Add("30 fps");
        CmbFps.Items.Add("60 fps");
        CmbFps.SelectedIndex = 0;

        // Quality
        CmbQuality.Items.Add("High  (CRF 18)");
        CmbQuality.Items.Add("Medium  (CRF 23)");
        CmbQuality.Items.Add("Low  (CRF 28)");
        CmbQuality.SelectedIndex = 0;
    }

    private void CmbResolution_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        switch (CmbResolution.SelectedIndex)
        {
            case 0: _selW = _projW; _selH = _projH; break;
            case 1: _selW = 1920;   _selH = 1080;   break;
            case 2: _selW = 1280;   _selH = 720;    break;
        }
    }

    private void CmbFps_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        switch (CmbFps.SelectedIndex)
        {
            case 0: _selFps = _projFps; break;
            case 1: _selFps = 30;       break;
            case 2: _selFps = 60;       break;
        }
    }

    private void CmbQuality_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selCrf = CmbQuality.SelectedIndex switch
        {
            1 => 23,
            2 => 28,
            _ => 18,
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        OutWidth  = _selW;
        OutHeight = _selH;
        Fps       = _selFps;
        Crf    = _selCrf;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}

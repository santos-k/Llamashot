using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Llamashot.Core;
using Llamashot.Core.VideoEditor;
using Line = System.Windows.Shapes.Line;
using Polygon = System.Windows.Shapes.Polygon;
using RadioButton = System.Windows.Controls.RadioButton;
using Orientation = System.Windows.Controls.Orientation;
using ColorConverter = System.Windows.Media.ColorConverter;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;

namespace Llamashot.Views;

public partial class VideoEditorWindow : Window
{
    private readonly TimelineProject _project = new();
    private readonly ObservableCollection<MediaAsset> _media = new();
    private const double RowH = 56, RulerH = 28, GripW = 7;

    private double _px = 50;               // pixels per second (zoom)
    private double _playhead;              // seconds
    private ClipItem? _sel;                // selected clip (PRIMARY — drives the inspector)
    private Track? _selTrack;
    private readonly HashSet<ClipItem> _selection = new();  // all selected clips (multi-select)
    private readonly List<ClipItem> _clipboard = new();     // copy/paste buffer
    private ClipItem? _previewClip;        // clip currently loaded in the player
    private bool _isPlaying, _muted, _suppressInsp, _ready;
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _cts;

    // drag state
    private enum DragMode { None, Move, TrimL, TrimR, Playhead }
    private DragMode _drag = DragMode.None;
    private double _dragStartX;
    private TimeSpan _dragOrigStart, _dragOrigIn, _dragOrigOut;
    private Border? _dragVis;

    // undo/redo
    private readonly Stack<string> _undo = new(), _redo = new();

    // autosave
    private bool _dirty;
    private readonly DispatcherTimer _autosaveTimer;

    public VideoEditorWindow()
    {
        InitializeComponent();
        MediaGrid.ItemsSource = _media;
        _project.Tracks.Add(new Track { Name = "Video Track 1", Kind = TrackKind.Video });
        _project.Tracks.Add(new Track { Name = "Audio Track 1", Kind = TrackKind.Audio });
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += Timer_Tick;
        _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autosaveTimer.Tick += AutosaveTimer_Tick;
        _autosaveTimer.Start();
        BuildTransitionPalette();
        _ready = true;
        RenderTimeline();
        UpdateInspector();
        UpdateUndoButtons();
        Loaded += VideoEditorWindow_Loaded;
        RefreshRecent();
        SwitchView(false);   // start on the Home view
    }

    // =============================================================== media

    private async void AddVideos_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import media",
            Multiselect = true,
            Filter = "Media|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.webm;*.m4v;*.flv;*.3gp;*.mpeg;*.mpg;*.mp3;*.wav;*.aac;*.m4a;*.flac;*.ogg;*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var path in dlg.FileNames) await ImportAsync(path);
    }

    private async Task<MediaAsset?> ImportAsync(string path)
    {
        try
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            ClipKind kind = FileToolsService.IsAudioExtension(ext) ? ClipKind.Audio
                : (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif") ? ClipKind.Image : ClipKind.Video;

            TimeSpan dur; int w = 0, h = 0;
            if (kind == ClipKind.Image) { dur = TimeSpan.FromSeconds(5); }
            else { var info = await FileToolsService.GetVideoInfoAsync(path); dur = info.duration <= TimeSpan.Zero ? TimeSpan.FromSeconds(3) : info.duration; w = info.width; h = info.height; }

            var asset = new MediaAsset { Path = path, Name = Path.GetFileName(path), Kind = kind, Duration = dur, Width = w, Height = h };
            _media.Add(asset);
            UpdateMediaEmpty();
            if (kind != ClipKind.Audio) _ = LoadThumbAsync(asset);
            return asset;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't import \"{Path.GetFileName(path)}\":\n{ex.Message}", "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    private static async Task LoadThumbAsync(MediaAsset asset)
    {
        string? thumb = asset.Kind == ClipKind.Image ? asset.Path : await FileToolsService.GenerateVideoThumbnailAsync(asset.Path, 200);
        if (thumb == null) return;
        var bmp = Load(thumb);
        if (bmp != null) asset.Thumb = bmp;
    }

    private static BitmapImage? Load(string path)
    {
        try { var b = new BitmapImage(); b.BeginInit(); b.UriSource = new Uri(path); b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze(); return b; }
        catch { return null; }
    }

    private void Media_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MediaGrid.SelectedItem is MediaAsset a) AddAssetToTimeline(a);
    }

    private void AddAssetToTimeline(MediaAsset a)
    {
        PushUndo();
        var track = a.Kind == ClipKind.Audio
            ? _project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Audio) ?? AddTrackInternal(TrackKind.Audio)
            : _project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Video) ?? AddTrackInternal(TrackKind.Video);

        double startAt = track.Clips.Count > 0 ? track.Clips.Max(c => c.End.TotalSeconds) : 0;
        var clip = new ClipItem
        {
            SourcePath = a.Path, Name = a.Name,
            Kind = a.Kind == ClipKind.Audio ? ClipKind.Audio : ClipKind.Video,
            SourceDuration = a.Duration, SrcIn = TimeSpan.Zero, SrcOut = a.Duration,
            Start = TimeSpan.FromSeconds(startAt), Thumb = a.Thumb,
        };
        track.Clips.Add(clip);
        RenderTimeline();
        SelectClip(clip, track);
        SyncPreview();   // load into the player so the first frame shows immediately
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (MediaGrid == null) return;
        string q = TxtSearch.Text.Trim();
        MediaGrid.Items.Filter = string.IsNullOrEmpty(q) ? null
            : o => o is MediaAsset m && m.Name.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateMediaEmpty() => MediaEmpty.Visibility = _media.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Rail_Changed(object sender, RoutedEventArgs e)
    {
        if (!(sender is RadioButton rb) || TxtPanelTitle == null) return;
        // Clicking any sidebar tab re-expands the panel if it was collapsed.
        if (_panelCollapsed) SetPanelCollapsed(false);
        string tab = rb.Content?.ToString() ?? "Media";
        bool live = tab is "Media" or "Audio";
        bool isTrans = tab == "Transitions";
        bool isText = tab == "Text";
        bool isEffects = tab == "Effects";
        TxtPanelTitle.Text = live ? "Project Media" : tab;
        TransScroll.Visibility = isTrans ? Visibility.Visible : Visibility.Collapsed;
        TextScroll.Visibility = isText ? Visibility.Visible : Visibility.Collapsed;
        EffectsScroll.Visibility = isEffects ? Visibility.Visible : Visibility.Collapsed;
        MediaScroll.Visibility = (isTrans || isText || isEffects) ? Visibility.Collapsed : Visibility.Visible;
        TxtComingSoon.Visibility = (live || isTrans || isText || isEffects) ? Visibility.Collapsed : Visibility.Visible;
        MediaGrid.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        if (live) { UpdateMediaEmpty(); MediaGrid.Items.Filter = tab == "Audio" ? o => o is MediaAsset m && m.Kind == ClipKind.Audio : null; }
        else MediaEmpty.Visibility = Visibility.Collapsed;
    }

    // =============================================================== shell (menus / view switch / panel / property tabs)

    private void EditMenu_Click(object sender, RoutedEventArgs e) => EditPopup.IsOpen = !EditPopup.IsOpen;
    private void ViewMenu_Click(object sender, RoutedEventArgs e) => ViewPopup.IsOpen = !ViewPopup.IsOpen;
    private void HelpMenu_Click(object sender, RoutedEventArgs e) => HelpPopup.IsOpen = !HelpPopup.IsOpen;
    private void About_Click(object sender, RoutedEventArgs e)
    {
        HelpPopup.IsOpen = false;
        MessageBox.Show(this, "Light Video Editor\nPart of Llamashot.\n\nA lightweight timeline editor: import, trim, arrange, add transitions, effects and titles, then export.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Switches between the Home view and the Editor view.</summary>
    private void SwitchView(bool showEditor)
    {
        EditorViewRoot.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        HomeViewRoot.Visibility = showEditor ? Visibility.Collapsed : Visibility.Visible;
    }

    private void GoHome_Click(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        SwitchView(false);
    }

    // ---- collapsible content panel ----
    private bool _panelCollapsed;
    private GridLength _panelWidth = new(300);
    private void PanelCollapse_Click(object sender, RoutedEventArgs e)
    {
        ViewPopup.IsOpen = false;
        SetPanelCollapsed(!_panelCollapsed);
    }
    private void SetPanelCollapsed(bool collapsed)
    {
        if (collapsed && !_panelCollapsed) _panelWidth = ColPanel.Width;
        _panelCollapsed = collapsed;
        ColPanel.Width = collapsed ? new GridLength(0) : (_panelWidth.Value > 0 ? _panelWidth : new GridLength(300));
        ColPanel.MinWidth = collapsed ? 0 : 200;
        if (PanelSplitter != null) PanelSplitter.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (BtnPanelCollapse != null) BtnPanelCollapse.Content = collapsed ? "›" : "‹";
    }

    // ---- Properties Video / Audio / Text tabs ----
    private void PropTab_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb) SetPropTab(rb.Tag?.ToString() ?? "Video");
    }
    private void SetPropTab(string tab)
    {
        if (InspVideo == null) return;
        InspVideo.Visibility = tab == "Video" ? Visibility.Visible : Visibility.Collapsed;
        InspAudio.Visibility = tab == "Audio" ? Visibility.Visible : Visibility.Collapsed;
        InspText.Visibility = tab == "Text" ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- toolbar tools that route to a sidebar tab / property section ----
    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        if (_sel != null && _selTrack?.Kind == TrackKind.Video && TabVideo != null) TabVideo.IsChecked = true;
        else MessageBox.Show(this, "Select a video clip to crop it (Crop lives under Properties ▸ Video).");
    }
    private void TextTool_Click(object sender, RoutedEventArgs e) { if (RbText != null) RbText.IsChecked = true; }
    private void TransitionTool_Click(object sender, RoutedEventArgs e) { if (RbTransitions != null) RbTransitions.IsChecked = true; }
    private void EffectTool_Click(object sender, RoutedEventArgs e) { if (RbEffects != null) RbEffects.IsChecked = true; }

    // ---- fullscreen (whole-window borderless; refined to preview-only in a later phase) ----
    private WindowStyle _prevStyle;
    private WindowState _prevState;
    private bool _fullscreen;
    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        ViewPopup.IsOpen = false;
        if (!_fullscreen)
        {
            _prevStyle = WindowStyle; _prevState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Normal;   // force a state change so Maximized covers the taskbar
            WindowState = WindowState.Maximized;
            _fullscreen = true;
        }
        else
        {
            WindowStyle = _prevStyle;
            WindowState = _prevState;
            _fullscreen = false;
        }
    }

    // =============================================================== Home view

    private int _pendingW = 1920, _pendingH = 1080;
    private List<RecentProject> _recentAll = new();

    private void HomeNav_Changed(object sender, RoutedEventArgs e)
    {
        if (HomePage == null) return; // during InitializeComponent
        bool home = NavHome.IsChecked == true;
        bool recent = NavRecent.IsChecked == true;
        bool templates = NavTemplates.IsChecked == true;
        bool samples = NavSamples.IsChecked == true;
        HomePage.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        RecentPage.Visibility = recent ? Visibility.Visible : Visibility.Collapsed;
        TemplatesPage.Visibility = templates ? Visibility.Visible : Visibility.Collapsed;
        SamplesPage.Visibility = samples ? Visibility.Visible : Visibility.Collapsed;
        if (recent) RefreshRecent();
    }

    private void PresetCard_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || HomeReso == null) return;
        var parts = (rb.Tag?.ToString() ?? "1920x1080").Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
        {
            _pendingW = w; _pendingH = h;
            string label = (w, h) switch
            {
                (1920, 1080) => "1920 × 1080 (Full HD)",
                (1080, 1920) => "1080 × 1920 (Portrait)",
                (1080, 1080) => "1080 × 1080 (Square)",
                (1440, 1080) => "1440 × 1080 (Standard)",
                (2560, 1080) => "2560 × 1080 (Cinema)",
                _ => $"{w} × {h}",
            };
            HomeReso.Text = label;
        }
    }

    private int HomeFpsValue() => HomeFps?.SelectedIndex switch { 0 => 24, 2 => 60, _ => 30 };
    private string HomeBgValue() => HomeBg?.SelectedIndex switch { 1 => "#FFFFFF", 2 => "#202020", _ => "#000000" };

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(HomeProjName?.Text) ? "Untitled Project" : HomeProjName.Text.Trim();
        StartNewProject(name, _pendingW, _pendingH, HomeFpsValue(), HomeBgValue());
        SwitchView(true);
    }

    private void Template_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b) return;
        var spec = (b.Tag?.ToString() ?? "Blank|1920x1080").Split('|');
        string name = spec.Length > 0 ? spec[0] : "Untitled Project";
        int w = 1920, h = 1080;
        if (spec.Length > 1)
        {
            var wh = spec[1].Split('x');
            if (wh.Length == 2) { int.TryParse(wh[0], out w); int.TryParse(wh[1], out h); }
        }
        StartNewProject(name, w, h, 30, "#000000");
        SwitchView(true);
    }

    /// <summary>Resets the (single, readonly) project to a blank one with the given settings.</summary>
    private void StartNewProject(string name, int w, int h, int fps, string bg)
    {
        StopPlayback();
        _project.Name = name;
        _project.CanvasW = w; _project.CanvasH = h; _project.Fps = fps; _project.BackgroundColor = bg;
        _project.Tracks.Clear();
        _project.Tracks.Add(new Track { Name = "Video Track 1", Kind = TrackKind.Video });
        _project.Tracks.Add(new Track { Name = "Audio Track 1", Kind = TrackKind.Audio });
        _media.Clear(); UpdateMediaEmpty();
        _undo.Clear(); _redo.Clear(); UpdateUndoButtons();
        _projectPath = null; _dirty = false; _playhead = 0;
        _loadedPath = null; _previewClip = null;
        if (TxtProjName != null) TxtProjName.Text = name;
        SelectClip(null, null);
        RenderTimeline();
    }

    private void HomeSettings_Click(object sender, RoutedEventArgs e) => Settings_Click(sender, e);

    // ---- recent projects ----
    private void RefreshRecent()
    {
        _recentAll = ProjectLibrary.Load().ToList();
        ApplyRecentFilter();
    }
    private void ApplyRecentFilter()
    {
        if (HomeRecentList == null) return;
        string q = RecentSearch?.Text?.Trim() ?? "";
        var view = string.IsNullOrEmpty(q)
            ? _recentAll
            : _recentAll.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                    || r.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        HomeRecentList.ItemsSource = view;
        if (RecentEmpty != null) RecentEmpty.Visibility = view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RecentSearch_Changed(object sender, TextChangedEventArgs e) => ApplyRecentFilter();

    private void RecentItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is RecentProject rp) LoadProjectFile(rp.Path);
    }
    private void RecentOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string path) LoadProjectFile(path);
    }
    private void RecentReveal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string path && File.Exists(path))
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }
    private void RecentRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string path) { ProjectLibrary.Remove(path); RefreshRecent(); }
    }

    /// <summary>Loads a .lsproj from disk and switches to the editor. Offers to drop missing entries.</summary>
    private void LoadProjectFile(string path)
    {
        if (!File.Exists(path))
        {
            if (MessageBox.Show(this, "That project file could not be found. Remove it from the recent list?",
                    "Open project", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            { ProjectLibrary.Remove(path); RefreshRecent(); }
            return;
        }
        try
        {
            Deserialize(File.ReadAllText(path));
            _projectPath = path; _dirty = false;
            if (TxtProjName != null) TxtProjName.Text = _project.Name;
            RenderTimeline();
            AddToRecent();
            SwitchView(true);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    /// <summary>Records the current saved project in the recent-projects library.</summary>
    private void AddToRecent()
    {
        if (_projectPath == null) return;
        try
        {
            ProjectLibrary.Add(new RecentProject(
                _projectPath, _project.Name, _project.CanvasW, _project.CanvasH, _project.Fps,
                _project.Duration.TotalSeconds, null, DateTime.Now));
        }
        catch { }
    }

    // =============================================================== media: sort / view / drag-out

    private void SortMenu_Click(object sender, RoutedEventArgs e) => SortPopup.IsOpen = !SortPopup.IsOpen;

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        SortPopup.IsOpen = false;
        string mode = (sender as FrameworkElement)?.Tag as string ?? "Name";
        var v = MediaGrid.Items;
        v.SortDescriptions.Clear();
        switch (mode)
        {
            case "Duration": v.SortDescriptions.Add(new SortDescription(nameof(MediaAsset.Duration), ListSortDirection.Descending)); break;
            case "Type":
                v.SortDescriptions.Add(new SortDescription(nameof(MediaAsset.Kind), ListSortDirection.Ascending));
                v.SortDescriptions.Add(new SortDescription(nameof(MediaAsset.Name), ListSortDirection.Ascending));
                break;
            case "Added": break;   // natural insertion order
            default: v.SortDescriptions.Add(new SortDescription(nameof(MediaAsset.Name), ListSortDirection.Ascending)); break;
        }
        v.Refresh();
    }

    private bool _listView;
    private void ToggleView_Click(object sender, RoutedEventArgs e)
    {
        _listView = !_listView;
        MediaGrid.ItemTemplate = (DataTemplate)FindResource(_listView ? "MediaListTpl" : "MediaGridTpl");
        MediaGrid.ItemsPanel = (ItemsPanelTemplate)FindResource(_listView ? "MediaStackTpl" : "MediaWrapTpl");
        BtnView.Content = _listView ? "▦" : "☰";
    }

    private Point _dragMediaStart;
    private MediaAsset? _dragMediaAsset;
    private void Media_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragMediaStart = e.GetPosition(null);
        _dragMediaAsset = (e.OriginalSource as FrameworkElement)?.DataContext as MediaAsset;
    }

    private void Media_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragMediaAsset == null) return;
        var p = e.GetPosition(null);
        if (Math.Abs(p.X - _dragMediaStart.X) < 8 && Math.Abs(p.Y - _dragMediaStart.Y) < 8) return;
        try { DragDrop.DoDragDrop(MediaGrid, new DataObject("LlamashotMedia", _dragMediaAsset), DragDropEffects.Copy); }
        catch { }
        _dragMediaAsset = null;
    }

    // =============================================================== timeline: drop target

    private void Timeline_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("LlamashotMedia") ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Timeline_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("LlamashotMedia") is not MediaAsset a) return;
        var pt = e.GetPosition(TimelineRoot);
        AddAssetAt(a, Math.Max(0, pt.X / _px), pt.Y);
    }

    // Drop media onto a specific track row at (roughly) the dropped time; magnetic reflow then orders it.
    private void AddAssetAt(MediaAsset a, double startSec, double y)
    {
        PushUndo();
        TrackKind wantKind = a.Kind == ClipKind.Audio ? TrackKind.Audio : TrackKind.Video;
        int rowIdx = (int)Math.Floor((y - RulerH) / RowH);
        Track? track = (rowIdx >= 0 && rowIdx < _project.Tracks.Count) ? _project.Tracks[rowIdx] : null;
        if (track == null || track.Kind != wantKind)
            track = _project.Tracks.FirstOrDefault(t => t.Kind == wantKind) ?? AddTrackInternal(wantKind);

        var clip = new ClipItem
        {
            SourcePath = a.Path, Name = a.Name,
            Kind = wantKind == TrackKind.Audio ? ClipKind.Audio : ClipKind.Video,
            SourceDuration = a.Duration, SrcIn = TimeSpan.Zero, SrcOut = a.Duration,
            Start = TimeSpan.FromSeconds(startSec), Thumb = a.Thumb,
        };
        track.Clips.Add(clip);
        ReflowTrack(track);
        RenderTimeline();
        SelectClip(clip, track);
        SyncPreview();   // load into the player so the first frame shows immediately
    }

    // =============================================================== transitions

    // VN-style palette: display label → ffmpeg xfade transition name.
    private static readonly (string Label, string Name)[] Transitions =
    {
        ("None", "none"), ("Fade", "fade"), ("To Black", "fadeblack"), ("To White", "fadewhite"),
        ("Dissolve", "dissolve"), ("Blur", "hblur"), ("Pixelize", "pixelize"),
        ("Slide L", "slideleft"), ("Slide R", "slideright"), ("Slide Up", "slideup"), ("Slide Down", "slidedown"),
        ("Wipe", "wipeleft"), ("Cover", "coverup"), ("Reveal", "revealup"),
        ("Circle In", "circleopen"), ("Circle Out", "circleclose"), ("Radial", "radial"),
        ("Zoom", "zoomin"), ("Squeeze", "squeezev"), ("Smooth", "smoothright"),
    };

    private static string TransitionLabel(string name) =>
        Transitions.FirstOrDefault(t => t.Name == name).Label is { } l && l.Length > 0 ? l : name;

    private void BuildTransitionPalette()
    {
        foreach (var (label, name) in Transitions)
        {
            var btn = new Button
            {
                Style = (Style)FindResource("Tool"), Content = label, Tag = name,
                Margin = new Thickness(0, 0, 5, 5), Padding = new Thickness(8, 5, 8, 5), MinWidth = 66,
            };
            btn.Click += Transition_Click;
            TransList.Children.Add(btn);
        }
    }

    private void Transition_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null || _selTrack?.Kind != TrackKind.Video) { MessageBox.Show(this, "Select a video clip first, then click a transition."); return; }
        string name = (sender as FrameworkElement)?.Tag as string ?? "none";
        var ordered = _selTrack.Clips.OrderBy(c => c.Start.TotalSeconds).ToList();
        int idx = ordered.IndexOf(_sel);
        if (name != "none" && idx >= ordered.Count - 1)
        {
            MessageBox.Show(this, "This is the last clip on its track — add a clip after it to place a transition.", "Transition");
            return;
        }
        PushUndo();
        _sel.Transition = name;
        _sel.TransitionDur = SldTransDur.Value;
        RenderTimeline();
        UpdateInspector();
    }

    private void TransDur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtTransDur != null) TxtTransDur.Text = $"{e.NewValue:0.0}s";
        if (_suppressInsp || _sel == null || _selTrack?.Kind != TrackKind.Video) return;
        if (_sel.Transition != "none") { _sel.TransitionDur = e.NewValue; RenderTimeline(); }
    }

    // =============================================================== effects

    private void Effect_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null || _selTrack?.Kind != TrackKind.Video) { MessageBox.Show(this, "Select a video clip first."); return; }
        PushUndo();
        _sel.EffectPreset = (string)((Button)sender).Tag;
        RenderTimeline();
    }

    private void Blur_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtBlur != null && SldBlur != null) TxtBlur.Text = ((int)SldBlur.Value).ToString();
        if (_suppressInsp || _sel == null || _selTrack?.Kind != TrackKind.Video) return;
        _sel.Blur = SldBlur.Value;
        RenderTimeline();
    }

    // =============================================================== text / titles

    private void AddTextPreset_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        var track = _project.Tracks.FirstOrDefault(t => t.Kind == TrackKind.Text) ?? AddTrackInternal(TrackKind.Text);
        string preset = (sender as FrameworkElement)?.Tag as string ?? "title";
        var dur = TimeSpan.FromSeconds(3);
        (string name, string text, double sizePct, double posY, string alignH, double posX) = preset switch
        {
            "subtitle" => ("Subtitle", "Subtitle", 6.0, 75.0, "C", 50.0),
            "lower" => ("Lower Third", "Lower third", 5.0, 82.0, "L", 12.0),
            _ => ("Title", "Your Title", 10.0, 50.0, "C", 50.0),
        };
        var clip = new ClipItem
        {
            Kind = ClipKind.Text, Name = name,
            SourceDuration = dur, SrcIn = TimeSpan.Zero, SrcOut = dur,
            Start = TimeSpan.FromSeconds(_playhead),
            Text = text, FontSizePct = sizePct, PosYPct = posY, AlignH = alignH, PosXPct = posX,
        };
        track.Clips.Add(clip);   // text clips sit anywhere — no reflow
        SelectClip(clip, track);
        RenderTimeline();
    }

    private void Text_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressInsp || _sel == null || _sel.Kind != ClipKind.Text) return;
        _sel.Text = InText.Text;
        _sel.FontFamily = (InFont.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? _sel.FontFamily;
        _sel.FontSizePct = ParseD(InFontSize.Text, _sel.FontSizePct);
        if (!string.IsNullOrWhiteSpace(InFontColor.Text)) _sel.FontColor = InFontColor.Text.Trim();
        _sel.Bold = InBold.IsChecked == true;
        _sel.PosXPct = ParseD(InTextX.Text, _sel.PosXPct);
        _sel.PosYPct = ParseD(InTextY.Text, _sel.PosYPct);
        _sel.AlignH = (InAlignH.SelectedItem as ComboBoxItem)?.Content?.ToString() switch { "Left" => "L", "Right" => "R", _ => "C" };
        _sel.AlignV = (InAlignV.SelectedItem as ComboBoxItem)?.Content?.ToString() switch { "Top" => "T", "Bottom" => "B", _ => "M" };
        _sel.BgBoxColor = InTextBox.IsChecked == true ? "#80000000" : null;
        _dirty = true;
        RenderTimeline();
    }
    private void Text_Changed(object sender, TextChangedEventArgs e) => Text_Changed(sender, (RoutedEventArgs)e);
    private void Text_Changed(object sender, SelectionChangedEventArgs e) => Text_Changed(sender, (RoutedEventArgs)e);

    private void TextSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null || _sel.Kind != ClipKind.Text) return;
        string hex = (sender as FrameworkElement)?.Tag as string ?? "#FFFFFF";
        InFontColor.Text = hex;   // triggers Text_Changed
    }

    // =============================================================== timeline render

    private double ContentWidth()
    {
        double sec = Math.Max(_project.Duration.TotalSeconds + 10, 30);
        return sec * _px;
    }

    private void RenderTimeline()
    {
        double cw = ContentWidth();
        DrawRuler(cw);
        TracksPanel.Children.Clear();
        TrackHeaders.Children.Clear();

        foreach (var track in _project.Tracks)
        {
            // header
            var head = new Border { Height = RowH, BorderBrush = (Brush)FindResource("BorderSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
            var hp = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            hp.Children.Add(new TextBlock { Text = track.Name, Foreground = (Brush)FindResource("TextSecondaryBrush"), FontSize = 11.5, FontWeight = FontWeights.SemiBold });
            var icons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
            var eye = new TextBlock { Text = track.Hidden ? "🚫" : "👁", FontSize = 12, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, ToolTip = "Show/Hide", Foreground = (Brush)FindResource("TextMutedBrush") };
            eye.MouseLeftButtonDown += (_, __) => { track.Hidden = !track.Hidden; RenderTimeline(); };
            var mute = new TextBlock { Text = track.Muted ? "🔇" : "🔊", FontSize = 12, Cursor = Cursors.Hand, ToolTip = "Mute", Foreground = (Brush)FindResource("TextMutedBrush") };
            mute.MouseLeftButtonDown += (_, __) => { track.Muted = !track.Muted; RenderTimeline(); };
            var del = new TextBlock { Text = "✕", FontSize = 12, Margin = new Thickness(10, 0, 0, 0), Cursor = Cursors.Hand, ToolTip = "Delete track", Foreground = (Brush)FindResource("TextMutedBrush") };
            var capturedTrack = track;
            del.MouseLeftButtonDown += (_, __) => DeleteTrack(capturedTrack);
            icons.Children.Add(eye); icons.Children.Add(mute); icons.Children.Add(del);
            hp.Children.Add(icons);
            head.Child = hp;
            TrackHeaders.Children.Add(head);

            // row
            var rowBg = track.Kind switch { TrackKind.Video => "#1C1C22", TrackKind.Audio => "#16221F", _ => "#241C2E" };
            var canvas = new Canvas { Height = RowH, Width = cw, Background = (Brush)new BrushConverter().ConvertFromString(rowBg)! };
            var rowBorder = new Border { Height = RowH, BorderBrush = (Brush)FindResource("BorderSoftBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = canvas };
            TracksPanel.Children.Add(rowBorder);

            for (int ci = 0; ci < track.Clips.Count; ci++)
            {
                var clip = track.Clips[ci];
                canvas.Children.Add(BuildClipVisual(clip, track));
                // transition badge at the boundary to the next clip
                if (track.Kind == TrackKind.Video && clip.Transition != "none" && ci < track.Clips.Count - 1)
                {
                    var chip = new Border
                    {
                        Background = (Brush)FindResource("AccentBrush"), CornerRadius = new CornerRadius(3),
                        Height = 16, Padding = new Thickness(3, 0, 3, 0),
                        ToolTip = $"{TransitionLabel(clip.Transition)} · {clip.TransitionDur:0.0}s",
                        Child = new TextBlock { Text = "⇄", Foreground = Brushes.White, FontSize = 10, VerticalAlignment = VerticalAlignment.Center },
                    };
                    Canvas.SetLeft(chip, clip.End.TotalSeconds * _px - 10);
                    Canvas.SetTop(chip, RowH / 2 - 8);
                    canvas.Children.Add(chip);
                }
            }
        }

        // overlay + playhead
        OverlayCanvas.Children.Clear();
        OverlayCanvas.Width = cw;
        OverlayCanvas.Height = RulerH + _project.Tracks.Count * RowH;
        var ph = new Rectangle { Width = 2, Height = OverlayCanvas.Height, Fill = Brushes.OrangeRed };
        Canvas.SetLeft(ph, _playhead * _px);
        OverlayCanvas.Children.Add(ph);
        var phHandle = new Polygon
        {
            Points = new PointCollection { new(0, 0), new(14, 0), new(7, 12) },
            Fill = Brushes.OrangeRed,
        };
        Canvas.SetLeft(phHandle, _playhead * _px - 6);
        Canvas.SetTop(phHandle, 0);
        OverlayCanvas.Children.Add(phHandle);

        UpdateStatus();
    }

    private void DrawRuler(double cw)
    {
        RulerCanvas.Children.Clear();
        RulerCanvas.Width = cw;
        // Pick a "nice" labelled interval targeting ~90px spacing. Ladder spans 0.5s … 2h so the
        // timeline stays readable from frame-level zoom right out to whole-recording overview.
        double targetSec = 90 / _px;
        double[] steps = { 0.5, 1, 2, 5, 10, 20, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };
        double step = steps.FirstOrDefault(s => s >= targetSec, 7200);
        var brush = (Brush)FindResource("TextMutedBrush");
        var minorBrush = (Brush)FindResource("BorderSoftBrush");

        // Minor (unlabelled) gridlines at 1/5 of the step, when there's room for them.
        double minor = step / 5.0;
        if (minor * _px >= 7)
            for (double t = 0; t * _px <= cw; t += minor)
            {
                double mx = t * _px;
                RulerCanvas.Children.Add(new Line { X1 = mx, X2 = mx, Y1 = 22, Y2 = 28, Stroke = minorBrush, StrokeThickness = 1 });
            }

        for (double t = 0; t * _px <= cw; t += step)
        {
            double x = t * _px;
            RulerCanvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = 15, Y2 = 28, Stroke = brush, StrokeThickness = 1 });
            RulerCanvas.Children.Add(new TextBlock
            {
                Text = TimelineProject.Fmt(TimeSpan.FromSeconds(t)),
                Foreground = brush, FontSize = 9.5,
                RenderTransform = new TranslateTransform(x + 3, 2),
            });
        }
    }

    private Border BuildClipVisual(ClipItem clip, Track track)
    {
        double left = clip.Start.TotalSeconds * _px;
        double w = Math.Max(6, clip.TimelineDuration.TotalSeconds * _px);
        bool selected = _selection.Contains(clip);
        bool primary = clip == _sel;
        bool isText = track.Kind == TrackKind.Text || clip.Kind == ClipKind.Text;
        var bg = isText
            ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#6D5AE6"), (Color)ColorConverter.ConvertFromString("#5647C4"), 90)
            : track.Kind == TrackKind.Video
                ? new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#2E4C6B"), (Color)ColorConverter.ConvertFromString("#24405C"), 90)
                : new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#1F5C4E"), (Color)ColorConverter.ConvertFromString("#1A4C40"), 90);

        var border = new Border
        {
            Width = w, Height = RowH - 8, CornerRadius = new CornerRadius(5),
            Background = bg, BorderThickness = new Thickness(selected && primary ? 3 : 2),
            BorderBrush = selected ? (Brush)FindResource("AccentBrush") : Brushes.Transparent,
            Cursor = Cursors.SizeAll, ClipToBounds = true, Tag = clip,
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, 4);

        var grid = new Grid();
        if (!isText && clip.Thumb != null && track.Kind == TrackKind.Video)
            grid.Children.Add(new Image { Source = clip.Thumb, Stretch = Stretch.UniformToFill, Opacity = 0.5 });
        if (!isText && track.Kind == TrackKind.Audio)
        {
            if (clip.Waveform != null)
                grid.Children.Add(new Image { Source = clip.Waveform, Stretch = Stretch.Fill, Opacity = 0.85 });
            EnsureWaveform(clip);
        }
        if (isText)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "T  " + clip.Text, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        else
        {
            grid.Children.Add(new TextBlock
            {
                Text = clip.Name, Foreground = Brushes.White, FontSize = 11, Margin = new Thickness(8, 3, 8, 0),
                VerticalAlignment = VerticalAlignment.Top, TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        // effect badge (top-left) for video clips with a preset or blur applied
        if (track.Kind == TrackKind.Video && !isText && (clip.EffectPreset != "none" || clip.Blur > 0.05))
        {
            string label = clip.EffectPreset switch { "bw" => "✦ B&W", "vintage" => "✦ Vintage", _ => "" };
            if (clip.Blur > 0.05) label = string.IsNullOrEmpty(label) ? "✦ Blur" : label + " · Blur";
            var fxChip = new Border
            {
                Background = (Brush)FindResource("AccentBrush"), CornerRadius = new CornerRadius(3),
                Height = 16, Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(4, 3, 0, 0), IsHitTestVisible = false,
                Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            };
            grid.Children.Add(fxChip);
        }
        // resize grips
        var gl = new Rectangle { Width = GripW, Fill = Brushes.White, Opacity = 0.35, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.SizeWE, Tag = "L" };
        var gr = new Rectangle { Width = GripW, Fill = Brushes.White, Opacity = 0.35, HorizontalAlignment = HorizontalAlignment.Right, Cursor = Cursors.SizeWE, Tag = "R" };
        grid.Children.Add(gl); grid.Children.Add(gr);
        border.Child = grid;

        border.MouseLeftButtonDown += Clip_MouseDown;
        border.MouseMove += Clip_MouseMove;
        border.MouseLeftButtonUp += Clip_MouseUp;
        return border;
    }

    // Lazily renders (and re-renders after a trim) the waveform image for an audio clip's current [SrcIn,SrcOut].
    private async void EnsureWaveform(ClipItem clip)
    {
        string key = $"{clip.SrcIn.Ticks}_{clip.SrcOut.Ticks}";
        if (clip.WaveKey == key && clip.Waveform != null) return;
        clip.WaveKey = key;
        try
        {
            string? p = await FileToolsService.GenerateWaveformAsync(clip.SourcePath, clip.SrcIn, clip.SrcOut, 900, 80);
            if (p == null || clip.WaveKey != key) return;
            var bmp = Load(p);
            if (bmp != null) { clip.Waveform = bmp; RenderTimeline(); }
        }
        catch { }
    }

    // =============================================================== clip drag / resize

    private void Clip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not ClipItem clip) return;
        TimelineRoot.Focus();
        var track = _project.Tracks.First(t => t.Clips.Contains(clip));
        if (track.Locked) return;

        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0)
        {
            // Ctrl+click: toggle this clip in the selection set (pure selection — no drag).
            if (!_selection.Add(clip)) _selection.Remove(clip);
            if (_selection.Contains(clip)) { _sel = clip; _selTrack = track; }
            else { _sel = _selection.FirstOrDefault(); _selTrack = _sel == null ? null : _project.Tracks.FirstOrDefault(t => t.Clips.Contains(_sel)); }
            RenderTimeline();
            UpdateInspector();
            e.Handled = true;
            return;
        }
        if ((mods & ModifierKeys.Shift) != 0)
        {
            // Shift+click: range-select on the clicked clip's track between the current primary and this clip.
            double a = (_sel != null && track.Clips.Contains(_sel)) ? _sel.Start.TotalSeconds : clip.Start.TotalSeconds;
            double bb = clip.Start.TotalSeconds;
            double lo = Math.Min(a, bb), hi = Math.Max(a, bb);
            foreach (var c in track.Clips)
                if (c.Start.TotalSeconds >= lo - 1e-6 && c.Start.TotalSeconds <= hi + 1e-6) _selection.Add(c);
            _sel = clip; _selTrack = track;
            RenderTimeline();
            UpdateInspector();
            e.Handled = true;
            return;
        }

        // Plain click: single-select (resets _selection to just this clip) and proceed to drag.
        SelectClip(clip, track);

        string? grip = (e.OriginalSource as FrameworkElement)?.Tag as string;
        _drag = grip == "L" ? DragMode.TrimL : grip == "R" ? DragMode.TrimR : DragMode.Move;
        _dragVis = b;
        _dragStartX = e.GetPosition(TimelineRoot).X;
        _dragOrigStart = clip.Start; _dragOrigIn = clip.SrcIn; _dragOrigOut = clip.SrcOut;
        _preDrag = Serialize();   // pre-gesture state for undo
        b.CaptureMouse();
        e.Handled = true;
    }

    // Magnetic timeline: pack a track's clips contiguously from 0 in their current visual order.
    private void ReflowTrack(Track? t)
    {
        if (t == null || t.Kind == TrackKind.Text) return;   // text clips can overlap / sit anywhere
        var ordered = t.Clips.OrderBy(c => c.Start.TotalSeconds).ToList();
        t.Clips.Clear();
        double cursor = 0;
        foreach (var c in ordered)
        {
            c.Start = TimeSpan.FromSeconds(cursor);
            cursor += c.TimelineDuration.TotalSeconds;
            t.Clips.Add(c);
        }
    }

    private void Clip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag == DragMode.None || _dragVis == null || _dragVis.Tag is not ClipItem clip) return;
        double dx = e.GetPosition(TimelineRoot).X - _dragStartX;
        double dsec = dx / _px;

        if (_drag == DragMode.Move)
        {
            double ns = Math.Max(0, _dragOrigStart.TotalSeconds + dsec);
            ns = Snap(ns, clip);
            clip.Start = TimeSpan.FromSeconds(ns);
            Canvas.SetLeft(_dragVis, ns * _px);
        }
        else if (_drag == DragMode.TrimL)
        {
            double ni = Math.Clamp(_dragOrigIn.TotalSeconds + dsec, 0, clip.SrcOut.TotalSeconds - 0.1);
            double delta = ni - _dragOrigIn.TotalSeconds;
            clip.SrcIn = TimeSpan.FromSeconds(ni);
            clip.Start = TimeSpan.FromSeconds(Math.Max(0, _dragOrigStart.TotalSeconds + delta));
            Canvas.SetLeft(_dragVis, clip.Start.TotalSeconds * _px);
            _dragVis.Width = Math.Max(6, clip.TimelineDuration.TotalSeconds * _px);
        }
        else if (_drag == DragMode.TrimR)
        {
            double no = Math.Clamp(_dragOrigOut.TotalSeconds + dsec, clip.SrcIn.TotalSeconds + 0.1, clip.SourceDuration.TotalSeconds);
            clip.SrcOut = TimeSpan.FromSeconds(no);
            _dragVis.Width = Math.Max(6, clip.TimelineDuration.TotalSeconds * _px);
        }
        UpdateInspector();
        UpdateStatus();
    }

    private void Clip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag == DragMode.None) return;
        if (sender is Border b) b.ReleaseMouseCapture();
        var mode = _drag;
        var clip = _dragVis?.Tag as ClipItem;
        var track = clip != null ? _project.Tracks.FirstOrDefault(t => t.Clips.Contains(clip)) : null;
        _drag = DragMode.None; _dragVis = null;

        // Cross-track move: if released over a different track of the same kind, relocate the clip there.
        if (mode == DragMode.Move && clip != null && track != null)
        {
            double y = e.GetPosition(TracksPanel).Y;
            int ti = (int)Math.Floor(y / RowH);
            if (ti >= 0 && ti < _project.Tracks.Count)
            {
                var target = _project.Tracks[ti];
                if (target != track && target.Kind == track.Kind && !target.Locked)
                {
                    track.Clips.Remove(clip);
                    target.Clips.Add(clip);
                    ReflowTrack(track);          // close the gap on the source track
                    track = target; _selTrack = target;
                }
            }
        }

        if (!string.IsNullOrEmpty(_preDrag)) { _undo.Push(_preDrag); _redo.Clear(); _preDrag = ""; UpdateUndoButtons(); }
        ReflowTrack(track);   // magnetic: close any gap the move/trim created
        RenderTimeline();
    }

    private string _preDrag = "";

    private double Snap(double sec, ClipItem moving)
    {
        double thresholdSec = 8 / _px;
        double best = sec; double bestDist = thresholdSec;
        void Try(double target) { double d = Math.Abs(sec - target); if (d < bestDist) { bestDist = d; best = target; } }
        Try(_playhead);
        Try(0);
        foreach (var t in _project.Tracks)
            foreach (var c in t.Clips)
            {
                if (c == moving) continue;
                Try(c.Start.TotalSeconds);
                Try(c.End.TotalSeconds);
            }
        return best;
    }

    // =============================================================== playhead / preview

    private void Timeline_MouseDown(object sender, MouseButtonEventArgs e)
    {
        TimelineRoot.Focus();
        double x = e.GetPosition(TimelineRoot).X;
        SeekTo(x / _px);
        _drag = DragMode.Playhead;
        TimelineRoot.CaptureMouse();
        TimelineRoot.MouseMove += Root_MouseMove;
        TimelineRoot.MouseLeftButtonUp += Root_MouseUp;
    }

    // The ruler is a dedicated scrub bar: it always moves the playhead, even where clips sit below it.
    private void Ruler_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SeekTo(Math.Max(0, e.GetPosition(RulerCanvas).X / _px));
        _drag = DragMode.Playhead;
        RulerCanvas.CaptureMouse();
        RulerCanvas.MouseMove += Ruler_MouseMove;
        RulerCanvas.MouseLeftButtonUp += Ruler_MouseUp;
        e.Handled = true;
    }

    private void Ruler_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag != DragMode.Playhead) return;
        SeekTo(Math.Max(0, e.GetPosition(RulerCanvas).X / _px));
    }

    private void Ruler_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _drag = DragMode.None;
        RulerCanvas.ReleaseMouseCapture();
        RulerCanvas.MouseMove -= Ruler_MouseMove;
        RulerCanvas.MouseLeftButtonUp -= Ruler_MouseUp;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag != DragMode.Playhead) return;
        SeekTo(Math.Max(0, e.GetPosition(TimelineRoot).X / _px));
    }

    private void Root_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _drag = DragMode.None;
        TimelineRoot.ReleaseMouseCapture();
        TimelineRoot.MouseMove -= Root_MouseMove;
        TimelineRoot.MouseLeftButtonUp -= Root_MouseUp;
    }

    private void SeekTo(double seconds)
    {
        _playhead = Math.Max(0, seconds);
        UpdatePlayheadVisual();
        SyncPreview();
        UpdateStatus();
    }

    private void UpdatePlayheadVisual()
    {
        double x = _playhead * _px;
        if (OverlayCanvas.Children.Count >= 2)
        {
            Canvas.SetLeft(OverlayCanvas.Children[0], x);
            Canvas.SetLeft(OverlayCanvas.Children[1], x - 6);
        }
    }

    private ClipItem? VideoClipAt(double sec) =>
        _project.Tracks.Where(t => t.Kind == TrackKind.Video && !t.Hidden)
            .SelectMany(t => t.Clips)
            .FirstOrDefault(c => sec >= c.Start.TotalSeconds && sec < c.End.TotalSeconds - 0.001);

    private TimeSpan _pendingPreviewPos;
    private string? _loadedPath;   // source currently handed to the MediaElement (avoids needless reloads)
    private void SyncPreview()
    {
        var clip = VideoClipAt(_playhead);
        if (clip == null)
        {
            TxtNoPreview.Visibility = _project.Tracks.Any(t => t.Clips.Count > 0) ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        TxtNoPreview.Visibility = Visibility.Collapsed;
        double into = _playhead - clip.Start.TotalSeconds;
        var pos = clip.SrcIn + TimeSpan.FromSeconds(Math.Max(0, into));
        _previewClip = clip;
        if (_loadedPath != clip.SourcePath)
        {
            _pendingPreviewPos = pos;
            _loadedPath = clip.SourcePath;
            Player.Source = new Uri(clip.SourcePath);
            Player.Pause();
        }
        else
        {
            try { Player.Position = pos; } catch { }
        }
    }

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        TxtNoPreview.Visibility = Visibility.Collapsed;
        try { Player.Position = _pendingPreviewPos; if (!_isPlaying) Player.Pause(); } catch { }
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) { }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _previewClip == null) return;
        var pos = Player.Position;
        if (pos >= _previewClip.SrcOut - TimeSpan.FromMilliseconds(40))
        {
            // advance to the next video clip by start time
            double nextStart = _previewClip.End.TotalSeconds;
            var next = _project.Tracks.Where(t => t.Kind == TrackKind.Video)
                .SelectMany(t => t.Clips).Where(c => c.Start.TotalSeconds >= nextStart - 0.05)
                .OrderBy(c => c.Start.TotalSeconds).FirstOrDefault();
            if (next == null) { StopPlayback(); SeekTo(_previewClip.End.TotalSeconds); return; }
            _previewClip = next; _playhead = next.Start.TotalSeconds;
            _pendingPreviewPos = next.SrcIn;
            if (_loadedPath != next.SourcePath) { _loadedPath = next.SourcePath; Player.Source = new Uri(next.SourcePath); }
            else { try { Player.Position = next.SrcIn; } catch { } }
            Player.Play();
        }
        else
        {
            _playhead = _previewClip.Start.TotalSeconds + (pos - _previewClip.SrcIn).TotalSeconds;
        }
        UpdatePlayheadVisual();
        UpdateStatus();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) { StopPlayback(); return; }
        var clip = VideoClipAt(_playhead) ?? _project.Tracks.Where(t => t.Kind == TrackKind.Video).SelectMany(t => t.Clips).OrderBy(c => c.Start.TotalSeconds).FirstOrDefault();
        if (clip == null) return;
        if (_playhead < clip.Start.TotalSeconds || _playhead >= clip.End.TotalSeconds) _playhead = clip.Start.TotalSeconds;
        _previewClip = clip;
        TxtNoPreview.Visibility = Visibility.Collapsed;
        var pos = clip.SrcIn + TimeSpan.FromSeconds(Math.Max(0, _playhead - clip.Start.TotalSeconds));
        _isPlaying = true; BtnPlay.Content = "⏸"; _timer.Start();
        if (_loadedPath == clip.SourcePath && Player.Source != null)   // already buffered → resume instantly, no reload hitch
        {
            try { Player.Position = pos; } catch { }
            Player.Play();
        }
        else
        {
            _pendingPreviewPos = pos;
            _loadedPath = clip.SourcePath;
            Player.Source = new Uri(clip.SourcePath);
            Player.Play();
        }
    }

    private void StopPlayback() { Player.Pause(); _isPlaying = false; BtnPlay.Content = "▶"; _timer.Stop(); }

    private void SkipStart_Click(object sender, RoutedEventArgs e) { StopPlayback(); SeekTo(0); }
    private void SkipEnd_Click(object sender, RoutedEventArgs e) { StopPlayback(); SeekTo(_project.Duration.TotalSeconds); }
    private void PrevFrame_Click(object sender, RoutedEventArgs e) { StopPlayback(); SeekTo(Math.Max(0, _playhead - 1.0 / _project.Fps)); }
    private void NextFrame_Click(object sender, RoutedEventArgs e) { StopPlayback(); SeekTo(_playhead + 1.0 / _project.Fps); }

    private void Mute_Click(object sender, RoutedEventArgs e) { _muted = !_muted; Player.IsMuted = _muted; BtnMute.Content = _muted ? "🔇" : "🔊"; }
    private void PreviewVol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (Player != null) Player.Volume = e.NewValue; }

    private void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_previewClip == null) { MessageBox.Show(this, "Nothing to capture.", "Snapshot"); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "PNG|*.png", FileName = "frame" };
        if (dlg.ShowDialog(this) != true) return;
        _ = SaveFrameAsync(dlg.FileName);
    }

    private async Task SaveFrameAsync(string outPath)
    {
        if (_previewClip == null) return;
        double into = Math.Max(0, _playhead - _previewClip.Start.TotalSeconds);
        var at = _previewClip.SrcIn + TimeSpan.FromSeconds(into);
        try
        {
            await FileToolsService.ExtractFrameAsync(_previewClip.SourcePath, at, outPath);
            MessageBox.Show(this, "Saved frame:\n" + outPath, "Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Snapshot failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    // =============================================================== selection / inspector

    private void SelectClip(ClipItem? clip, Track? track)
    {
        _sel = clip; _selTrack = track;
        _selection.Clear();
        if (clip != null) _selection.Add(clip);
        RenderTimeline();
        UpdateInspector();
    }

    private void UpdateInspector()
    {
        _suppressInsp = true;
        if (_sel == null)
        {
            TxtInspTarget.Text = "No clip selected";
            TabVideo.IsChecked = TabAudio.IsChecked = TabText.IsChecked = false;
            SetPropTab("");
        }
        else if (_selTrack?.Kind == TrackKind.Text || _sel.Kind == ClipKind.Text)
        {
            TxtInspTarget.Text = _sel.Name;
            TabVideo.IsChecked = TabAudio.IsChecked = false;
            TabText.IsChecked = true;
            SetPropTab("Text");
            InText.Text = _sel.Text;
            SelectComboByContent(InFont, _sel.FontFamily);
            InFontSize.Text = _sel.FontSizePct.ToString("0.##");
            InFontColor.Text = _sel.FontColor;
            InBold.IsChecked = _sel.Bold;
            InTextX.Text = ((int)_sel.PosXPct).ToString();
            InTextY.Text = ((int)_sel.PosYPct).ToString();
            SelectComboByContent(InAlignH, _sel.AlignH switch { "L" => "Left", "R" => "Right", _ => "Center" });
            SelectComboByContent(InAlignV, _sel.AlignV switch { "T" => "Top", "B" => "Bottom", _ => "Middle" });
            InTextBox.IsChecked = _sel.BgBoxColor != null;
        }
        else
        {
            TxtInspTarget.Text = _sel.Name;
            bool isVideo = _selTrack?.Kind == TrackKind.Video;
            if (isVideo) TabVideo.IsChecked = true; else TabAudio.IsChecked = true;
            SetPropTab(isVideo ? "Video" : "Audio");
            if (isVideo)
            {
                InScale.Text = ((int)_sel.Scale).ToString();
                InPosX.Text = ((int)_sel.PosX).ToString();
                InPosY.Text = ((int)_sel.PosY).ToString();
                InRotate.Text = ((int)_sel.Rotate).ToString();
                InOpacity.Text = ((int)_sel.Opacity).ToString();
                InFlipH.IsChecked = _sel.FlipH; InFlipV.IsChecked = _sel.FlipV;
                InSpeed.Value = _sel.Speed; TxtSpeed.Text = $"{_sel.Speed:0.00}x";
                InFadeIn.Text = _sel.FadeIn.ToString("0.##");
                InFadeOut.Text = _sel.FadeOut.ToString("0.##");
                InBright.Value = _sel.Brightness; TxtBright.Text = ((int)_sel.Brightness).ToString();
                InContrast.Value = _sel.Contrast; TxtContrast.Text = ((int)_sel.Contrast).ToString();
                InSat.Value = _sel.Saturation; TxtSat.Text = ((int)_sel.Saturation).ToString();
                TxtInspTrans.Text = _sel.Transition == "none" ? "None" : $"{TransitionLabel(_sel.Transition)} · {_sel.TransitionDur:0.0}s";
                SldTransDur.Value = _sel.TransitionDur <= 0 ? 0.5 : _sel.TransitionDur;
                SldBlur.Value = _sel.Blur; TxtBlur.Text = ((int)_sel.Blur).ToString();
                InReverse.IsChecked = _sel.Reverse;
                InCropL.Text = ((int)_sel.CropL).ToString();
                InCropT.Text = ((int)_sel.CropT).ToString();
                InCropR.Text = ((int)_sel.CropR).ToString();
                InCropB.Text = ((int)_sel.CropB).ToString();
                InSpeedFrom.Text = "0:00";
                InSpeedTo.Text = TimelineProject.Fmt(_sel.TimelineDuration);
            }
            InVol.Value = _sel.Volume; TxtVol.Text = $"{(int)_sel.Volume}%";
            InspAudioFade.Visibility = isVideo ? Visibility.Collapsed : Visibility.Visible;
            InAFadeIn.Text = _sel.FadeIn.ToString("0.##");
            InAFadeOut.Text = _sel.FadeOut.ToString("0.##");
        }
        _suppressInsp = false;
    }

    private void Insp_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressInsp || _sel == null) return;
        _sel.Scale = ParseD(InScale.Text, _sel.Scale);
        _sel.PosX = ParseD(InPosX.Text, _sel.PosX);
        _sel.PosY = ParseD(InPosY.Text, _sel.PosY);
        _sel.Rotate = ParseD(InRotate.Text, _sel.Rotate);
        _sel.Opacity = ParseD(InOpacity.Text, _sel.Opacity);
        _sel.FlipH = InFlipH.IsChecked == true; _sel.FlipV = InFlipV.IsChecked == true;
        if (_selTrack?.Kind == TrackKind.Video)
        {
            _sel.FadeIn = ParseD(InFadeIn.Text, _sel.FadeIn);
            _sel.FadeOut = ParseD(InFadeOut.Text, _sel.FadeOut);
        }
        else
        {
            _sel.FadeIn = ParseD(InAFadeIn.Text, _sel.FadeIn);
            _sel.FadeOut = ParseD(InAFadeOut.Text, _sel.FadeOut);
        }
    }
    private void Insp_Changed(object sender, TextChangedEventArgs e) => Insp_Changed(sender, (RoutedEventArgs)e);

    private void Reverse_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressInsp || _sel == null || _selTrack?.Kind != TrackKind.Video) return;
        if (InReverse.IsChecked == true && _sel.TimelineDuration.TotalSeconds > 30)
            MessageBox.Show(this, "Reversing long clips can be slow and memory-heavy.", "Reverse clip", MessageBoxButton.OK, MessageBoxImage.Information);
        PushUndo();
        _sel.Reverse = InReverse.IsChecked == true;
        RenderTimeline();
    }

    private void Crop_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressInsp || _sel == null || _selTrack?.Kind != TrackKind.Video) return;
        _sel.CropL = ParseD(InCropL.Text, _sel.CropL);
        _sel.CropT = ParseD(InCropT.Text, _sel.CropT);
        _sel.CropR = ParseD(InCropR.Text, _sel.CropR);
        _sel.CropB = ParseD(InCropB.Text, _sel.CropB);
        RenderTimeline();
    }

    private void Color_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtBright != null && InBright != null) TxtBright.Text = ((int)InBright.Value).ToString();
        if (TxtContrast != null && InContrast != null) TxtContrast.Text = ((int)InContrast.Value).ToString();
        if (TxtSat != null && InSat != null) TxtSat.Text = ((int)InSat.Value).ToString();
        if (_suppressInsp || _sel == null) return;
        _sel.Brightness = InBright.Value; _sel.Contrast = InContrast.Value; _sel.Saturation = InSat.Value;
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null || _selTrack?.Kind != TrackKind.Video) { MessageBox.Show(this, "Select a video clip first."); return; }
        string tag = (sender as FrameworkElement)?.Tag as string ?? "none";
        (double b, double c, double s) = tag switch
        {
            "bw" => (0.0, 110.0, 0.0),
            "warm" => (8.0, 108.0, 125.0),
            "cool" => (-4.0, 105.0, 90.0),
            "vivid" => (5.0, 120.0, 160.0),
            _ => (0.0, 100.0, 100.0),
        };
        PushUndo();
        _suppressInsp = true;
        InBright.Value = b; InContrast.Value = c; InSat.Value = s;
        _suppressInsp = false;
        _sel.Brightness = b; _sel.Contrast = c; _sel.Saturation = s;
        TxtBright.Text = ((int)b).ToString(); TxtContrast.Text = ((int)c).ToString(); TxtSat.Text = ((int)s).ToString();
    }

    private void Speed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSpeed != null) TxtSpeed.Text = $"{e.NewValue:0.00}x";
        if (_suppressInsp || _sel == null) return;
        _sel.Speed = e.NewValue; ReflowTrack(_selTrack); RenderTimeline();
    }

    private void Vol_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtVol != null) TxtVol.Text = $"{(int)e.NewValue}%";
        if (_suppressInsp || _sel == null) return;
        _sel.Volume = e.NewValue;
    }

    // Speed ramp: apply a new speed to just the [from,to] section (in timeline seconds within the clip)
    // by splitting the clip into up to three pieces and speeding only the middle one.
    private void SpeedRange_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null || _selTrack?.Kind != TrackKind.Video) { MessageBox.Show(this, "Select a video clip first."); return; }
        double dur = _sel.TimelineDuration.TotalSeconds;
        double from = ParseTime(InSpeedFrom.Text, 0);
        double to = ParseTime(InSpeedTo.Text, dur);
        double x = ParseD(InSpeedX.Text, 2);
        from = Math.Clamp(from, 0, dur); to = Math.Clamp(to, 0, dur);
        if (to - from < 0.1) { MessageBox.Show(this, "Pick a section of at least 0.1s (\"To\" after \"From\")."); return; }
        if (x < 0.25 || x > 4) { MessageBox.Show(this, "Speed must be between 0.25× and 4×."); return; }
        ApplySpeedRange(_sel, _selTrack, from, to, x);
    }

    private void ApplySpeedRange(ClipItem clip, Track track, double fromSec, double toSec, double speed)
    {
        PushUndo();
        double dur = clip.TimelineDuration.TotalSeconds;
        double origSpeed = clip.Speed <= 0 ? 1 : clip.Speed;
        var srcIn = clip.SrcIn; var srcOut = clip.SrcOut;
        var cut1 = srcIn + TimeSpan.FromSeconds(fromSec * origSpeed);   // source time at section start
        var cut2 = srcIn + TimeSpan.FromSeconds(toSec * origSpeed);     // source time at section end
        string outgoing = clip.Transition; double outgoingDur = clip.TransitionDur;

        var pieces = new List<ClipItem>();
        if (fromSec > 0.02) { var a = clip.Clone(); a.SrcIn = srcIn; a.SrcOut = cut1; a.Speed = origSpeed; a.Transition = "none"; pieces.Add(a); }
        var b = clip.Clone(); b.SrcIn = cut1; b.SrcOut = cut2; b.Speed = speed; b.Transition = "none"; pieces.Add(b);
        if (toSec < dur - 0.02) { var c = clip.Clone(); c.SrcIn = cut2; c.SrcOut = srcOut; c.Speed = origSpeed; c.Transition = "none"; pieces.Add(c); }

        // Fades belong only to the outer edges; the original outgoing transition stays on the last piece.
        for (int i = 0; i < pieces.Count; i++)
        {
            if (i != 0) pieces[i].FadeIn = 0;
            if (i != pieces.Count - 1) pieces[i].FadeOut = 0;
        }
        pieces[^1].Transition = outgoing; pieces[^1].TransitionDur = outgoingDur;

        int idx = track.Clips.IndexOf(clip);
        track.Clips.Remove(clip);
        for (int i = 0; i < pieces.Count; i++) track.Clips.Insert(idx + i, pieces[i]);
        ReflowTrack(track);
        RenderTimeline();
        SelectClip(b, track);
    }

    private static double ParseD(string s, double fallback) => double.TryParse(s, out var v) ? v : fallback;

    private static void SelectComboByContent(ComboBox cb, string content)
    {
        foreach (var it in cb.Items)
            if (it is ComboBoxItem ci && string.Equals(ci.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
            { cb.SelectedItem = ci; return; }
        cb.SelectedIndex = 0;
    }

    // Accepts plain seconds ("135"), mm:ss ("2:15") or h:mm:ss.
    private static double ParseTime(string s, double fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        s = s.Trim();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var ns = System.Globalization.NumberStyles.Any;
        if (s.Contains(':'))
        {
            double total = 0;
            foreach (var seg in s.Split(':'))
            {
                if (!double.TryParse(seg, ns, ci, out var val)) return fallback;
                total = total * 60 + val;
            }
            return total;
        }
        return double.TryParse(s, ns, ci, out var v) ? v : fallback;
    }

    // =============================================================== editing ops

    private Track AddTrackInternal(TrackKind kind)
    {
        int n = _project.Tracks.Count(t => t.Kind == kind) + 1;
        string kindName = kind switch { TrackKind.Video => "Video", TrackKind.Audio => "Audio", _ => "Text" };
        var t = new Track { Name = $"{kindName} Track {n}", Kind = kind };
        _project.Tracks.Add(t);
        return t;
    }

    private void AddTrack_Click(object sender, RoutedEventArgs e)
    {
        PushUndo();
        AddTrackInternal(TrackKind.Video);
        RenderTimeline();
    }

    private void DeleteTrack(Track track)
    {
        if (_project.Tracks.Count <= 1) { MessageBox.Show(this, "You need at least one track.", "Delete track"); return; }
        if (track.Clips.Count > 0 &&
            MessageBox.Show(this, $"Delete \"{track.Name}\" and its {track.Clips.Count} clip(s)?", "Delete track",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        PushUndo();
        if (_selTrack == track) { _sel = null; _selTrack = null; UpdateInspector(); }
        _project.Tracks.Remove(track);
        RenderTimeline();
    }

    private void Split_Click(object sender, RoutedEventArgs e)
    {
        var clip = VideoClipAt(_playhead) ?? (_sel != null && _playhead > _sel.Start.TotalSeconds && _playhead < _sel.End.TotalSeconds ? _sel : null);
        Track? track = clip == null ? null : _project.Tracks.FirstOrDefault(t => t.Clips.Contains(clip));
        // also allow splitting audio clip under playhead
        if (clip == null)
        {
            foreach (var t in _project.Tracks)
                foreach (var c in t.Clips)
                    if (_playhead > c.Start.TotalSeconds + 0.05 && _playhead < c.End.TotalSeconds - 0.05) { clip = c; track = t; break; }
        }
        if (clip == null || track == null) { MessageBox.Show(this, "Move the playhead over a clip, then Split.", "Split"); return; }

        PushUndo();
        double into = (_playhead - clip.Start.TotalSeconds) * clip.Speed; // source seconds from SrcIn
        var cutSrc = clip.SrcIn + TimeSpan.FromSeconds(into);
        var second = clip.Clone();
        second.SrcIn = cutSrc;
        second.Start = TimeSpan.FromSeconds(_playhead);
        clip.SrcOut = cutSrc;
        int i = track.Clips.IndexOf(clip);
        track.Clips.Insert(i + 1, second);
        ReflowTrack(track);
        SelectClip(second, track);
        RenderTimeline();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selection.Count > 1)
        {
            PushUndo();
            var affected = new HashSet<Track>();
            foreach (var c in _selection.ToList())
            {
                var t = _project.Tracks.FirstOrDefault(t => t.Clips.Contains(c));
                if (t == null) continue;
                t.Clips.Remove(c);
                if (t.Kind != TrackKind.Text) affected.Add(t);
            }
            foreach (var t in affected) ReflowTrack(t);
            SelectClip(null, null);
            RenderTimeline();
            return;
        }
        if (_sel == null || _selTrack == null) return;
        PushUndo();
        var track = _selTrack;
        track.Clips.Remove(_sel);
        ReflowTrack(track);   // magnetic: shift following clips left to close the gap
        SelectClip(null, null);
        RenderTimeline();
    }

    private void RippleDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_sel == null || _selTrack == null) return;
        PushUndo();
        var gap = _sel.TimelineDuration;
        double from = _sel.Start.TotalSeconds;
        _selTrack.Clips.Remove(_sel);
        foreach (var c in _selTrack.Clips.Where(c => c.Start.TotalSeconds > from).OrderBy(c => c.Start.TotalSeconds))
            c.Start = TimeSpan.FromSeconds(Math.Max(0, c.Start.TotalSeconds - gap.TotalSeconds));
        SelectClip(null, null);
        RenderTimeline();
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (_selection.Count > 1)
        {
            PushUndo();
            var affected = new HashSet<Track>();
            var clones = new List<ClipItem>();
            foreach (var c in _selection.ToList())
            {
                var t = _project.Tracks.FirstOrDefault(t => t.Clips.Contains(c));
                if (t == null) continue;
                var dup = c.Clone();
                dup.Start = c.End;
                int i = t.Clips.IndexOf(c);
                t.Clips.Insert(i + 1, dup);
                clones.Add(dup);
                if (t.Kind != TrackKind.Text) affected.Add(t);
            }
            foreach (var t in affected) ReflowTrack(t);
            // select the new clones (primary = first)
            _selection.Clear();
            foreach (var c in clones) _selection.Add(c);
            _sel = clones.FirstOrDefault();
            _selTrack = _sel == null ? null : _project.Tracks.FirstOrDefault(t => t.Clips.Contains(_sel));
            RenderTimeline();
            UpdateInspector();
            return;
        }
        if (_sel == null || _selTrack == null) return;
        PushUndo();
        var one = _sel.Clone();
        one.Start = _sel.End;
        int idx = _selTrack.Clips.IndexOf(_sel);
        _selTrack.Clips.Insert(idx + 1, one);
        ReflowTrack(_selTrack);
        SelectClip(one, _selTrack);
        RenderTimeline();
    }

    // =============================================================== copy / paste

    private void CopySelection()
    {
        _clipboard.Clear();
        foreach (var c in _selection) _clipboard.Add(c.Clone());
    }

    private void PasteClipboard()
    {
        if (_clipboard.Count == 0) return;
        PushUndo();
        double anchor = _clipboard.Min(c => c.Start.TotalSeconds);
        var affected = new HashSet<Track>();
        var pasted = new List<ClipItem>();
        foreach (var src in _clipboard)
        {
            var clone = src.Clone();
            clone.Start = TimeSpan.FromSeconds(_playhead + (src.Start.TotalSeconds - anchor));
            TrackKind wantKind = clone.Kind switch { ClipKind.Audio => TrackKind.Audio, ClipKind.Text => TrackKind.Text, _ => TrackKind.Video };
            var track = _project.Tracks.FirstOrDefault(t => t.Kind == wantKind) ?? AddTrackInternal(wantKind);
            track.Clips.Add(clone);
            pasted.Add(clone);
            if (track.Kind != TrackKind.Text) affected.Add(track);
        }
        foreach (var t in affected) ReflowTrack(t);
        _selection.Clear();
        foreach (var c in pasted) _selection.Add(c);
        _sel = pasted.FirstOrDefault();
        _selTrack = _sel == null ? null : _project.Tracks.FirstOrDefault(t => t.Clips.Contains(_sel));
        RenderTimeline();
        UpdateInspector();
    }

    private void Trim_Click(object sender, RoutedEventArgs e) => TrimPopup.IsOpen = !TrimPopup.IsOpen;

    private void TrimStart_Click(object sender, RoutedEventArgs e)
    {
        TrimPopup.IsOpen = false;
        if (_sel == null) { MessageBox.Show(this, "Select a clip first."); return; }
        if (_playhead <= _sel.Start.TotalSeconds || _playhead >= _sel.End.TotalSeconds) { MessageBox.Show(this, "Put the playhead inside the selected clip."); return; }
        PushUndo();
        double into = (_playhead - _sel.Start.TotalSeconds) * _sel.Speed;
        _sel.SrcIn += TimeSpan.FromSeconds(into);
        _sel.Start = TimeSpan.FromSeconds(_playhead);
        ReflowTrack(_selTrack); RenderTimeline(); UpdateInspector();
    }

    private void TrimEnd_Click(object sender, RoutedEventArgs e)
    {
        TrimPopup.IsOpen = false;
        if (_sel == null) { MessageBox.Show(this, "Select a clip first."); return; }
        if (_playhead <= _sel.Start.TotalSeconds || _playhead >= _sel.End.TotalSeconds) { MessageBox.Show(this, "Put the playhead inside the selected clip."); return; }
        PushUndo();
        double into = (_playhead - _sel.Start.TotalSeconds) * _sel.Speed;
        _sel.SrcOut = _sel.SrcIn + TimeSpan.FromSeconds(into);
        ReflowTrack(_selTrack); RenderTimeline(); UpdateInspector();
    }

    private void TrimMiddleHint_Click(object sender, RoutedEventArgs e)
    {
        TrimPopup.IsOpen = false;
        MessageBox.Show(this, "To cut out the middle:\n1. Playhead where the unwanted part begins → Split.\n2. Playhead where it ends → Split.\n3. Select the middle piece → Ripple Delete (closes the gap).",
            "Trim middle", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =============================================================== zoom

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { _px = e.NewValue; if (!_ready) return; RenderTimeline(); UpdatePlayheadVisual(); }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SldZoom.Value = Math.Min(SldZoom.Maximum, SldZoom.Value * 1.4 + 0.15);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SldZoom.Value = Math.Max(SldZoom.Minimum, SldZoom.Value / 1.4);

    // Zoom so the whole timeline fits the visible area without horizontal scrolling.
    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        double avail = TimelineScroll.ViewportWidth;
        if (avail < 50) avail = Math.Max(200, ActualWidth - 160);
        // Match ContentWidth()'s span so content width == viewport width (no leftover scroll).
        double sec = Math.Max(_project.Duration.TotalSeconds + 10, 30);
        double px = Math.Clamp(avail / sec, SldZoom.Minimum, SldZoom.Maximum);
        SldZoom.Value = px;   // triggers Zoom_Changed → re-render
    }

    private void TimelineScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(HeaderScroll.VerticalOffset - e.VerticalOffset) > 0.5) HeaderScroll.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void UpdateStatus()
    {
        TxtStatus.Text = $"Duration {TimelineProject.Fmt(_project.Duration)}   •   Playhead {TimelineProject.Fmt(TimeSpan.FromSeconds(_playhead))}";
        string zoomTxt = _px < 10 ? _px.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : ((int)_px).ToString();
        TxtStatusRight.Text = $"Zoom {zoomTxt} px/s   •   {_project.Fps} fps   •   {_project.CanvasW}×{_project.CanvasH}";
        TxtTime.Text = $"{TimelineProject.FmtLong(TimeSpan.FromSeconds(_playhead))} / {TimelineProject.FmtLong(_project.Duration)}";
        TxtCanvas.Text = $"{_project.CanvasW}×{_project.CanvasH}";
    }

    // =============================================================== undo / redo

    private void PushUndo() { _preDrag = ""; _undo.Push(Serialize()); _redo.Clear(); UpdateUndoButtons(); _dirty = true; }
    private void UpdateUndoButtons() { BtnUndo.IsEnabled = _undo.Count > 0; BtnRedo.IsEnabled = _redo.Count > 0; }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        _redo.Push(Serialize());
        Deserialize(_undo.Pop());
        SelectClip(null, null); RenderTimeline(); UpdateUndoButtons();
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        _undo.Push(Serialize());
        Deserialize(_redo.Pop());
        SelectClip(null, null); RenderTimeline(); UpdateUndoButtons();
    }

    // =============================================================== autosave

    /// <summary>
    /// Returns the app-data directory using the same resolution as ProjectLibrary
    /// (honours ProjectLibrary.DataDirOverride so tests can redirect it).
    /// </summary>
    private static string AutosaveDataDir =>
        ProjectLibrary.DataDirOverride
        ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Llamashot");

    private static string UnsavedRecoveryPath =>
        System.IO.Path.Combine(AutosaveDataDir, "recovery.autosave");

    private static string AutosavePathFor(string projectPath) => projectPath + ".autosave";

    /// <summary>Deletes the autosave for a given project path, or the unsaved recovery if null.</summary>
    private static void DeleteAutosave(string? projectPath)
    {
        try
        {
            string path = projectPath != null ? AutosavePathFor(projectPath) : UnsavedRecoveryPath;
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private void AutosaveTimer_Tick(object? sender, EventArgs e)
    {
        if (!_dirty) return;
        bool hasClips = _project.Tracks.Any(t => t.Clips.Count > 0);
        if (!hasClips) return;

        try
        {
            string json = Serialize();
            string target = _projectPath != null
                ? AutosavePathFor(_projectPath)
                : UnsavedRecoveryPath;

            if (_projectPath == null)
                Directory.CreateDirectory(AutosaveDataDir);

            File.WriteAllText(target, json);
            _dirty = false;
            if (TxtAutosave != null)
                TxtAutosave.Text = "Auto Save: " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch { /* autosave must never crash or interrupt the user */ }
    }

    private void VideoEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Check for an unsaved-project recovery file on startup
        string recoveryPath = UnsavedRecoveryPath;
        if (_projectPath == null && File.Exists(recoveryPath))
        {
            var res = MessageBox.Show(this,
                "A more recent auto-saved version was found. Recover it?",
                "Recover Auto-Save", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    Deserialize(File.ReadAllText(recoveryPath));
                    RenderTimeline();
                    _dirty = false;
                }
                catch { /* ignore corrupt recovery */ }
            }
            else
            {
                try { File.Delete(recoveryPath); } catch { }
            }
        }
    }

    // =============================================================== project save / load

    private sealed record ClipDto(string Path, string Name, int Kind, double SrcIn, double SrcOut, double Start, double Speed, double Volume, double Scale, double PosX, double PosY, double Rotate, double Opacity, bool FlipH, bool FlipV, double SourceDur,
        double FadeIn = 0, double FadeOut = 0, double Brightness = 0, double Contrast = 100, double Saturation = 100,
        string Transition = "none", double TransitionDur = 0.5,
        string Text = "Title", string FontFamily = "Segoe UI", double FontSizePct = 8, string FontColor = "#FFFFFF", bool Bold = true,
        string AlignH = "C", string AlignV = "M", double PosXPct = 50, double PosYPct = 50, string? BgBoxColor = null,
        double Blur = 0, string EffectPreset = "none",
        bool Reverse = false, double CropL = 0, double CropT = 0, double CropR = 0, double CropB = 0);
    private sealed record TrackDto(string Name, int Kind, List<ClipDto> Clips);
    private sealed record ProjectDto(string Name, int Fps, int CanvasW, int CanvasH, List<TrackDto> Tracks, string BackgroundColor = "#000000");

    private string Serialize()
    {
        var dto = new ProjectDto(_project.Name, _project.Fps, _project.CanvasW, _project.CanvasH,
            _project.Tracks.Select(t => new TrackDto(t.Name, (int)t.Kind,
                t.Clips.Select(c => new ClipDto(c.SourcePath, c.Name, (int)c.Kind, c.SrcIn.TotalSeconds, c.SrcOut.TotalSeconds,
                    c.Start.TotalSeconds, c.Speed, c.Volume, c.Scale, c.PosX, c.PosY, c.Rotate, c.Opacity, c.FlipH, c.FlipV, c.SourceDuration.TotalSeconds,
                    c.FadeIn, c.FadeOut, c.Brightness, c.Contrast, c.Saturation, c.Transition, c.TransitionDur,
                    c.Text, c.FontFamily, c.FontSizePct, c.FontColor, c.Bold, c.AlignH, c.AlignV, c.PosXPct, c.PosYPct, c.BgBoxColor,
                    c.Blur, c.EffectPreset,
                    c.Reverse, c.CropL, c.CropT, c.CropR, c.CropB)).ToList())).ToList(),
            BackgroundColor: _project.BackgroundColor);
        return JsonSerializer.Serialize(dto);
    }

    private void Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var dto = JsonSerializer.Deserialize<ProjectDto>(json);
        if (dto == null) return;
        _project.Name = dto.Name; _project.Fps = dto.Fps; _project.CanvasW = dto.CanvasW; _project.CanvasH = dto.CanvasH; _project.BackgroundColor = dto.BackgroundColor;
        TxtProjName.Text = dto.Name;
        _project.Tracks.Clear();
        foreach (var t in dto.Tracks)
        {
            var track = new Track { Name = t.Name, Kind = (TrackKind)t.Kind };
            foreach (var c in t.Clips)
            {
                var clip = new ClipItem
                {
                    SourcePath = c.Path, Name = c.Name, Kind = (ClipKind)c.Kind, SourceDuration = TimeSpan.FromSeconds(c.SourceDur),
                    SrcIn = TimeSpan.FromSeconds(c.SrcIn), SrcOut = TimeSpan.FromSeconds(c.SrcOut), Start = TimeSpan.FromSeconds(c.Start),
                    Speed = c.Speed, Volume = c.Volume, Scale = c.Scale, PosX = c.PosX, PosY = c.PosY, Rotate = c.Rotate, Opacity = c.Opacity, FlipH = c.FlipH, FlipV = c.FlipV,
                    FadeIn = c.FadeIn, FadeOut = c.FadeOut, Brightness = c.Brightness, Contrast = c.Contrast, Saturation = c.Saturation,
                    Transition = c.Transition, TransitionDur = c.TransitionDur,
                    Text = c.Text, FontFamily = c.FontFamily, FontSizePct = c.FontSizePct, FontColor = c.FontColor, Bold = c.Bold,
                    AlignH = c.AlignH, AlignV = c.AlignV, PosXPct = c.PosXPct, PosYPct = c.PosYPct, BgBoxColor = c.BgBoxColor,
                    Blur = c.Blur, EffectPreset = c.EffectPreset,
                    Reverse = c.Reverse, CropL = c.CropL, CropT = c.CropT, CropR = c.CropR, CropB = c.CropB,
                };
                if (clip.Kind == ClipKind.Video && File.Exists(clip.SourcePath))
                    _ = LoadClipThumbAsync(clip);
                track.Clips.Add(clip);
            }
            _project.Tracks.Add(track);
        }
    }

    private static async Task LoadClipThumbAsync(ClipItem clip)
    {
        string? thumb = await FileToolsService.GenerateVideoThumbnailAsync(clip.SourcePath, 200);
        if (thumb != null) clip.Thumb = Load(thumb);
    }

    private void File_Click(object sender, RoutedEventArgs e) => FilePopup.IsOpen = !FilePopup.IsOpen;
    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        if (MessageBox.Show(this, "Start a new project? Unsaved changes will be lost.", "New Project", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        _project.Tracks.Clear();
        _project.Tracks.Add(new Track { Name = "Video Track 1", Kind = TrackKind.Video });
        _project.Tracks.Add(new Track { Name = "Audio Track 1", Kind = TrackKind.Audio });
        _media.Clear(); UpdateMediaEmpty(); _undo.Clear(); _redo.Clear(); UpdateUndoButtons();
        _playhead = 0; SelectClip(null, null); RenderTimeline();
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Llamashot project|*.lsproj|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            string chosenPath = dlg.FileName;
            string autosavePath = chosenPath + ".autosave";
            bool usedAutosave = false;
            if (File.Exists(autosavePath) &&
                File.GetLastWriteTime(autosavePath) > File.GetLastWriteTime(chosenPath))
            {
                var res = MessageBox.Show(this,
                    "A more recent auto-saved version was found. Recover it?",
                    "Recover Auto-Save", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    Deserialize(File.ReadAllText(autosavePath));
                    usedAutosave = true;
                }
                else
                {
                    try { File.Delete(autosavePath); } catch { }
                }
            }
            if (!usedAutosave)
                Deserialize(File.ReadAllText(chosenPath));
            _projectPath = chosenPath;
            _dirty = false;
            if (TxtProjName != null) TxtProjName.Text = _project.Name;
            RenderTimeline();
            AddToRecent();
            SwitchView(true);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private string? _projectPath;
    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        _project.Name = TxtProjName.Text;
        if (_projectPath == null) { SaveProjectAs_Click(sender, e); return; }
        try
        {
            File.WriteAllText(_projectPath, Serialize());
            _dirty = false;
            // Delete any lingering autosave for this path
            DeleteAutosave(_projectPath);
            AddToRecent();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        _project.Name = TxtProjName.Text;
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Llamashot project|*.lsproj", FileName = _project.Name };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, Serialize());
            _projectPath = dlg.FileName;
            _dirty = false;
            // Delete the unsaved recovery file now that we have a real path
            DeleteAutosave(null);
            AddToRecent();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try { new SettingsWindow { Owner = this }.ShowDialog(); } catch { }
    }

    // =============================================================== export

    private void Export_Click(object sender, RoutedEventArgs e) => ExportPopup.IsOpen = !ExportPopup.IsOpen;
    private void ExportMp4_Click(object sender, RoutedEventArgs e) { ExportPopup.IsOpen = false; _ = DoExport("mp4", false); }
    private void ExportMkv_Click(object sender, RoutedEventArgs e) { ExportPopup.IsOpen = false; _ = DoExport("mkv", false); }
    private void ExportAudio_Click(object sender, RoutedEventArgs e) { ExportPopup.IsOpen = false; _ = DoExport("mp3", true); }

    private async Task DoExport(string ext, bool audioOnly)
    {
        var videoTrackClips = _project.Tracks.Where(t => t.Kind == TrackKind.Video && !t.Hidden)
            .SelectMany(t => t.Clips).OrderBy(c => c.Start.TotalSeconds).ToList();
        if (videoTrackClips.Count == 0) { MessageBox.Show(this, "Add at least one clip to a video track.", "Export"); return; }

        var vids = videoTrackClips.Select(c => new FileToolsService.TimelineVideoClip(
            c.SourcePath, c.SrcIn, c.SrcOut, c.Speed, c.Volume, c.FlipH, c.FlipV, c.Rotate,
            Scale: c.Scale, PosX: c.PosX, PosY: c.PosY, Opacity: c.Opacity,
            FadeIn: c.FadeIn, FadeOut: c.FadeOut,
            Brightness: c.Brightness, Contrast: c.Contrast, Saturation: c.Saturation,
            Transition: c.Transition, TransitionDur: c.TransitionDur,
            Blur: c.Blur, EffectPreset: c.EffectPreset,
            Reverse: c.Reverse, CropL: c.CropL, CropT: c.CropT, CropR: c.CropR, CropB: c.CropB)).ToList();
        var auds = _project.Tracks.Where(t => t.Kind == TrackKind.Audio && !t.Muted)
            .SelectMany(t => t.Clips)
            .Select(c => new FileToolsService.TimelineAudioClip(c.SourcePath, c.SrcIn, c.SrcOut, c.Start, c.Volume, c.FadeIn, c.FadeOut)).ToList();
        var texts = _project.Tracks.Where(t => t.Kind == TrackKind.Text && !t.Hidden)
            .SelectMany(t => t.Clips).Where(c => c.Kind == ClipKind.Text)
            .Select(c => new FileToolsService.TimelineTextClip(c.Text, c.FontFamily, c.FontSizePct, c.FontColor, c.Bold, c.AlignH, c.AlignV, c.PosXPct, c.PosYPct, c.BgBoxColor, c.Start.TotalSeconds, c.End.TotalSeconds)).ToList();

        // For video exports, ask the user for resolution/fps/quality before the save dialog.
        int exportW = _project.CanvasW, exportH = _project.CanvasH, exportFps = _project.Fps, exportCrf = 18;
        if (!audioOnly)
        {
            var optDlg = new ExportOptionsWindow(_project.CanvasW, _project.CanvasH, _project.Fps) { Owner = this };
            if (optDlg.ShowDialog() != true) return;
            exportW = optDlg.OutWidth; exportH = optDlg.OutHeight; exportFps = optDlg.Fps; exportCrf = optDlg.Crf;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = $"{ext.ToUpper()}|*.{ext}", FileName = _project.Name.Replace(' ', '_') };
        if (dlg.ShowDialog(this) != true) return;

        StopPlayback();
        _cts = new CancellationTokenSource();
        var progress = new Progress<int>(v => PrgBusy.Value = v);
        TxtBusy.Text = "Exporting video…"; PrgBusy.Value = 0; BusyOverlay.Visibility = Visibility.Visible;
        try
        {
            await FileToolsService.ExportTimelineAsync(vids, auds, audioOnly, dlg.FileName, progress, _cts.Token,
                canvasW: exportW, canvasH: exportH, fps: exportFps, crf: exportCrf, textClips: texts);
            BusyOverlay.Visibility = Visibility.Collapsed;
            var info = new FileInfo(dlg.FileName);
            if (MessageBox.Show(this, $"Saved {Path.GetFileName(dlg.FileName)} ({FileToolsService.FormatFileSize(info.Length)}).\n\nOpen its folder?",
                    "Export complete", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dlg.FileName}\"") { UseShellExecute = true }); } catch { }
        }
        catch (OperationCanceledException) { BusyOverlay.Visibility = Visibility.Collapsed; try { if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName); } catch { } }
        catch (Exception ex) { BusyOverlay.Visibility = Visibility.Collapsed; MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { _cts?.Dispose(); _cts = null; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // =============================================================== mouse wheel: scroll + zoom

    // Ctrl+wheel = zoom (anchored under the cursor); Shift+wheel = horizontal scroll;
    // plain wheel = vertical scroll when tracks overflow, else horizontal along the timeline.
    private void Timeline_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0)
        {
            double secUnderCursor = e.GetPosition(TimelineRoot).X / _px;
            double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            double newPx = Math.Clamp(_px * factor, SldZoom.Minimum, SldZoom.Maximum);
            if (Math.Abs(newPx - _px) > 0.0001)
            {
                double mouseX = e.GetPosition(TimelineScroll).X;
                SldZoom.Value = newPx;                       // triggers Zoom_Changed → re-render (updates _px)
                double target = secUnderCursor * _px - mouseX;
                TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, target));
            }
            e.Handled = true;
        }
        else if ((mods & ModifierKeys.Shift) != 0)
        {
            TimelineScroll.ScrollToHorizontalOffset(TimelineScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
        else if (TimelineScroll.ScrollableHeight < 1)        // no vertical overflow → scroll the timeline sideways
        {
            TimelineScroll.ScrollToHorizontalOffset(TimelineScroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
        // else: fall through to the ScrollViewer's default vertical scroll
    }

    // True horizontal-wheel support (precision trackpad two-finger sideways swipe → WM_MOUSEHWHEEL).
    private const int WM_MOUSEHWHEEL = 0x020E;
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEHWHEEL && TimelineScroll != null && TimelineScroll.IsMouseOver)
        {
            int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);   // >0 = tilt/swipe right
            TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, TimelineScroll.HorizontalOffset + delta));
            handled = true;
        }
        return IntPtr.Zero;
    }

    // =============================================================== keys

    // Tab / Shift+Tab cycles the selected clip along the timeline (intercepted before focus traversal).
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || BusyOverlay.Visibility == Visibility.Visible) return;
        if (Keyboard.FocusedElement is TextBox) return;     // let Tab move between text fields while typing
        if (e.Key == Key.Tab)
        {
            CycleClip((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : +1);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || BusyOverlay.Visibility == Visibility.Visible) return;
        if (Keyboard.FocusedElement is TextBox) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0, shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (e.Key == Key.Space) { PlayPause_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.S && !ctrl) { Split_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.Delete || e.Key == Key.Back) { Delete_Click(this, new()); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { Duplicate_Click(this, new()); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { CopySelection(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { PasteClipboard(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { SaveProject_Click(this, new()); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z && !shift) { Undo_Click(this, new()); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift))) { Redo_Click(this, new()); e.Handled = true; }
        else if (ctrl && e.Key == Key.E) { _ = DoExport("mp4", false); e.Handled = true; }
        else if (e.Key == Key.F && !ctrl) { Fullscreen_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.Escape && _fullscreen) { Fullscreen_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.OemPlus || e.Key == Key.Add) { ZoomIn_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.OemMinus || e.Key == Key.Subtract) { ZoomOut_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.Left || (e.Key == Key.OemComma)) { StepPlayhead(-1, shift, ctrl); e.Handled = true; }
        else if (e.Key == Key.Right || (e.Key == Key.OemPeriod)) { StepPlayhead(+1, shift, ctrl); e.Handled = true; }
        else if (e.Key == Key.Home) { StopPlayback(); SeekTo(0); ScrollToPlayhead(); e.Handled = true; }
        else if (e.Key == Key.End) { StopPlayback(); SeekTo(_project.Duration.TotalSeconds); ScrollToPlayhead(); e.Handled = true; }
    }

    // =============================================================== navigation helpers

    // All clips flattened in playback order (by start, then track order) for Tab cycling.
    private List<(ClipItem clip, Track track)> FlatClips() =>
        _project.Tracks.SelectMany(t => t.Clips.Select(c => (clip: c, track: t)))
            .OrderBy(x => x.clip.Start.TotalSeconds)
            .ThenBy(x => _project.Tracks.IndexOf(x.track))
            .ToList();

    private void CycleClip(int dir)
    {
        var all = FlatClips();
        if (all.Count == 0) return;
        int idx = _sel == null ? -1 : all.FindIndex(x => x.clip == _sel);
        int next = idx < 0 ? (dir > 0 ? 0 : all.Count - 1) : (((idx + dir) % all.Count) + all.Count) % all.Count;
        var (clip, track) = all[next];
        SelectClip(clip, track);
        StopPlayback();
        SeekTo(clip.Start.TotalSeconds);
        ScrollClipIntoView(clip);
    }

    private void StepPlayhead(int dir, bool shift, bool ctrl)
    {
        StopPlayback();
        if (ctrl) SeekTo(NextEdge(dir));                                  // jump to next/prev clip edge (cut point)
        else { double step = shift ? 1.0 : 1.0 / Math.Max(1, _project.Fps); SeekTo(Math.Max(0, _playhead + dir * step)); }
        ScrollToPlayhead();
    }

    private double NextEdge(int dir)
    {
        var edges = new List<double> { 0 };
        foreach (var t in _project.Tracks)
            foreach (var c in t.Clips) { edges.Add(c.Start.TotalSeconds); edges.Add(c.End.TotalSeconds); }
        edges.Sort();
        if (dir > 0) return edges.FirstOrDefault(x => x > _playhead + 1e-4, _playhead);
        double result = 0;
        foreach (var x in edges) if (x < _playhead - 1e-4) result = x;
        return result;
    }

    // Keep the playhead comfortably inside the viewport as it moves.
    private void ScrollToPlayhead()
    {
        double x = _playhead * _px;
        double left = TimelineScroll.HorizontalOffset, right = left + TimelineScroll.ViewportWidth;
        if (x < left + 20) TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, x - 40));
        else if (x > right - 20) TimelineScroll.ScrollToHorizontalOffset(x - TimelineScroll.ViewportWidth + 60);
    }

    private void ScrollClipIntoView(ClipItem c)
    {
        double l = c.Start.TotalSeconds * _px, r = c.End.TotalSeconds * _px;
        double vl = TimelineScroll.HorizontalOffset, vr = vl + TimelineScroll.ViewportWidth;
        if (l < vl) TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, l - 30));
        else if (r > vr) TimelineScroll.ScrollToHorizontalOffset(Math.Max(0, r - TimelineScroll.ViewportWidth + 30));
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel(); _timer?.Stop();
        try { Player?.Close(); } catch { }
        base.OnClosed(e);
    }
}

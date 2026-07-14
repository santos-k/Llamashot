using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Llamashot.Core.VideoEditor;

public enum TrackKind { Video, Audio, Text }
public enum ClipKind { Video, Audio, Image, Text }

/// <summary>A media asset imported into the project (shown in the Project Media panel).</summary>
public sealed class MediaAsset : INotifyPropertyChanged
{
    public string Path { get; init; } = "";
    public string Name { get; init; } = "";
    public ClipKind Kind { get; init; }
    public TimeSpan Duration { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    private BitmapImage? _thumb;
    public BitmapImage? Thumb { get => _thumb; set { _thumb = value; Raise(); } }

    public string DurationText => TimelineProject.Fmt(Duration);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}

/// <summary>One clip placed on a track: a source trimmed to [SrcIn, SrcOut], positioned at Start.</summary>
public sealed class ClipItem : INotifyPropertyChanged
{
    public string SourcePath { get; init; } = "";
    public string Name { get; init; } = "";
    public ClipKind Kind { get; init; }
    public TimeSpan SourceDuration { get; init; }

    private TimeSpan _srcIn, _srcOut, _start;
    public TimeSpan SrcIn { get => _srcIn; set { _srcIn = value; Raise(); Raise(nameof(TimelineDuration)); } }
    public TimeSpan SrcOut { get => _srcOut; set { _srcOut = value; Raise(); Raise(nameof(TimelineDuration)); } }
    public TimeSpan Start { get => _start; set { _start = value; Raise(); Raise(nameof(End)); } }

    private double _speed = 1.0, _volume = 100, _scale = 100, _posX, _posY, _rotate, _opacity = 100;
    private double _fadeIn, _fadeOut, _brightness, _contrast = 100, _saturation = 100;
    private string _transition = "none";
    private double _transitionDur = 0.5;
    private bool _flipH, _flipV;
    private double _blur;
    private string _effect = "none";
    // text (used when Kind == Text)
    private string _text = "Title";
    private string _fontFamily = "Segoe UI";
    private double _fontSizePct = 8;              // % of canvas height
    private string _fontColor = "#FFFFFF";
    private bool _bold = true;
    private string _alignH = "C";                // L / C / R
    private string _alignV = "M";                // T / M / B
    private double _posXPct = 50, _posYPct = 50;  // centre point, 0..100 of canvas
    private string? _bgBoxColor;                  // null = no background box
    public double Speed { get => _speed; set { _speed = value <= 0 ? 1 : value; Raise(); Raise(nameof(TimelineDuration)); Raise(nameof(End)); } }
    public double Volume { get => _volume; set { _volume = value; Raise(); } }
    public double Scale { get => _scale; set { _scale = value; Raise(); } }
    public double PosX { get => _posX; set { _posX = value; Raise(); } }
    public double PosY { get => _posY; set { _posY = value; Raise(); } }
    public double Rotate { get => _rotate; set { _rotate = value; Raise(); } }
    public double Opacity { get => _opacity; set { _opacity = value; Raise(); } }
    public double FadeIn { get => _fadeIn; set { _fadeIn = value < 0 ? 0 : value; Raise(); } }
    public double FadeOut { get => _fadeOut; set { _fadeOut = value < 0 ? 0 : value; Raise(); } }
    public double Brightness { get => _brightness; set { _brightness = value; Raise(); } }
    public double Contrast { get => _contrast; set { _contrast = value; Raise(); } }
    public double Saturation { get => _saturation; set { _saturation = value; Raise(); } }
    /// <summary>xfade transition name applied between this clip and the NEXT clip on its track ("none" = hard cut).</summary>
    public string Transition { get => _transition; set { _transition = string.IsNullOrEmpty(value) ? "none" : value; Raise(); } }
    public double TransitionDur { get => _transitionDur; set { _transitionDur = value < 0 ? 0 : value; Raise(); } }
    public bool FlipH { get => _flipH; set { _flipH = value; Raise(); } }
    public bool FlipV { get => _flipV; set { _flipV = value; Raise(); } }
    /// <summary>Gaussian/box blur amount (0..25); 0 = no blur.</summary>
    public double Blur { get => _blur; set { _blur = Math.Clamp(value, 0, 25); Raise(); } }
    /// <summary>Effect preset applied to the clip: "none", "bw", "vintage".</summary>
    public string EffectPreset { get => _effect; set { _effect = string.IsNullOrEmpty(value) ? "none" : value; Raise(); } }
    // --- text ---
    public string Text { get => _text; set { _text = value ?? ""; Raise(); } }
    public string FontFamily { get => _fontFamily; set { _fontFamily = string.IsNullOrEmpty(value) ? "Segoe UI" : value; Raise(); } }
    public double FontSizePct { get => _fontSizePct; set { _fontSizePct = Math.Clamp(value, 1, 50); Raise(); } }
    public string FontColor { get => _fontColor; set { _fontColor = string.IsNullOrEmpty(value) ? "#FFFFFF" : value; Raise(); } }
    public bool Bold { get => _bold; set { _bold = value; Raise(); } }
    public string AlignH { get => _alignH; set { _alignH = string.IsNullOrEmpty(value) ? "C" : value; Raise(); } }
    public string AlignV { get => _alignV; set { _alignV = string.IsNullOrEmpty(value) ? "M" : value; Raise(); } }
    public double PosXPct { get => _posXPct; set { _posXPct = Math.Clamp(value, 0, 100); Raise(); } }
    public double PosYPct { get => _posYPct; set { _posYPct = Math.Clamp(value, 0, 100); Raise(); } }
    public string? BgBoxColor { get => _bgBoxColor; set { _bgBoxColor = value; Raise(); } }

    private BitmapImage? _thumb;
    public BitmapImage? Thumb { get => _thumb; set { _thumb = value; Raise(); } }

    private BitmapImage? _waveform;
    public BitmapImage? Waveform { get => _waveform; set { _waveform = value; Raise(); } }
    /// <summary>Tracks which [SrcIn,SrcOut] the current waveform image was rendered for.</summary>
    public string? WaveKey { get; set; }

    /// <summary>Duration on the timeline (source span compressed/expanded by Speed).</summary>
    public TimeSpan TimelineDuration => TimeSpan.FromSeconds(Math.Max(0.02, (SrcOut - SrcIn).TotalSeconds / (Speed <= 0 ? 1 : Speed)));
    public TimeSpan End => Start + TimelineDuration;

    public ClipItem Clone() => new()
    {
        SourcePath = SourcePath, Name = Name, Kind = Kind, SourceDuration = SourceDuration,
        SrcIn = SrcIn, SrcOut = SrcOut, Start = Start, Speed = Speed, Volume = Volume,
        Scale = Scale, PosX = PosX, PosY = PosY, Rotate = Rotate, Opacity = Opacity,
        FadeIn = FadeIn, FadeOut = FadeOut, Brightness = Brightness, Contrast = Contrast, Saturation = Saturation,
        Transition = Transition, TransitionDur = TransitionDur,
        FlipH = FlipH, FlipV = FlipV, Blur = Blur, EffectPreset = EffectPreset,
        Thumb = Thumb, Waveform = Waveform, WaveKey = WaveKey,
        Text = Text, FontFamily = FontFamily, FontSizePct = FontSizePct, FontColor = FontColor, Bold = Bold,
        AlignH = AlignH, AlignV = AlignV, PosXPct = PosXPct, PosYPct = PosYPct, BgBoxColor = BgBoxColor,
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}

public sealed class Track : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public TrackKind Kind { get; init; }
    public ObservableCollection<ClipItem> Clips { get; } = new();

    private bool _muted, _hidden, _locked;
    public bool Muted { get => _muted; set { _muted = value; Raise(); } }
    public bool Hidden { get => _hidden; set { _hidden = value; Raise(); } }
    public bool Locked { get => _locked; set { _locked = value; Raise(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}

public sealed class TimelineProject
{
    public string Name { get; set; } = "Untitled Project";
    public int Fps { get; set; } = 30;
    public int CanvasW { get; set; } = 1920;
    public int CanvasH { get; set; } = 1080;
    public string BackgroundColor { get; set; } = "#000000";
    public ObservableCollection<Track> Tracks { get; } = new();

    public TimeSpan Duration
    {
        get
        {
            double max = 0;
            foreach (var t in Tracks)
                foreach (var c in t.Clips)
                    max = Math.Max(max, c.End.TotalSeconds);
            return TimeSpan.FromSeconds(max);
        }
    }

    public static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    public static string FmtLong(TimeSpan t) => t.ToString(@"hh\:mm\:ss");
}

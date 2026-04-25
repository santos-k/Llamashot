using System.IO;
using System.Windows.Threading;
using DrawBitmap = System.Drawing.Bitmap;
using DrawImaging = System.Drawing.Imaging;

namespace Llamashot.Core;

public class ScreenRecorder : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly int _fps;
    private readonly int _maxSeconds;
    private int _regionX, _regionY, _regionW, _regionH;
    private DateTime _startTime;
    private TimeSpan _pausedElapsed;
    private bool _recording;
    private bool _paused;
    private bool _disposed;
    private string? _framesDir;
    private int _frameCount;

    public bool IsRecording => _recording;
    public bool IsPaused => _paused;
    public int FramesCaptured => _frameCount;

    public TimeSpan Elapsed
    {
        get
        {
            if (!_recording) return _pausedElapsed;
            if (_paused) return _pausedElapsed;
            return _pausedElapsed + (DateTime.Now - _startTime);
        }
    }

    public event Action? OnTick;
    public event Action? OnMaxReached;

    public ScreenRecorder(int fps = 10, int maxSeconds = 120)
    {
        _fps = fps;
        _maxSeconds = maxSeconds;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000.0 / fps) };
        _timer.Tick += CaptureFrame;
    }

    public bool Start(int x, int y, int width, int height)
    {
        if (_recording) return false;

        _regionX = x;
        _regionY = y;
        // H.264 requires even dimensions
        _regionW = width % 2 == 0 ? width : width - 1;
        _regionH = height % 2 == 0 ? height : height - 1;
        _frameCount = 0;
        _pausedElapsed = TimeSpan.Zero;

        _framesDir = Path.Combine(Path.GetTempPath(), $"llamashot_rec_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(_framesDir);

        _startTime = DateTime.Now;
        _recording = true;
        _paused = false;
        _timer.Start();
        return true;
    }

    public void Pause()
    {
        if (!_recording || _paused) return;
        _paused = true;
        _pausedElapsed += DateTime.Now - _startTime;
        _timer.Stop();
    }

    public void Resume()
    {
        if (!_recording || !_paused) return;
        _paused = false;
        _startTime = DateTime.Now;
        _timer.Start();
    }

    public void Stop()
    {
        if (!_recording) return;
        if (!_paused)
            _pausedElapsed += DateTime.Now - _startTime;

        _recording = false;
        _paused = false;
        _timer.Stop();
    }

    public async Task<bool> SaveAsMp4Async(string outputPath)
    {
        if (_framesDir == null || _frameCount == 0) return false;

        try
        {
            var composition = new Windows.Media.Editing.MediaComposition();
            var frameDuration = TimeSpan.FromMilliseconds(1000.0 / _fps);

            for (int i = 0; i < _frameCount; i++)
            {
                var framePath = Path.Combine(_framesDir, $"frame_{i:D6}.jpg");
                if (!File.Exists(framePath)) continue;

                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(framePath);
                var clip = await Windows.Media.Editing.MediaClip.CreateFromImageFileAsync(file, frameDuration);
                composition.Clips.Add(clip);
            }

            if (composition.Clips.Count == 0) return false;

            var quality = _regionW >= 1920
                ? Windows.Media.MediaProperties.VideoEncodingQuality.HD1080p
                : _regionW >= 1280
                    ? Windows.Media.MediaProperties.VideoEncodingQuality.HD720p
                    : Windows.Media.MediaProperties.VideoEncodingQuality.Vga;

            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(quality);
            // Ensure even dimensions for H.264
            profile.Video.Width = (uint)(_regionW % 2 == 0 ? _regionW : _regionW - 1);
            profile.Video.Height = (uint)(_regionH % 2 == 0 ? _regionH : _regionH - 1);

            var dir = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(dir);

            var outputFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(dir);
            var outputFile = await outputFolder.CreateFileAsync(
                Path.GetFileName(outputPath),
                Windows.Storage.CreationCollisionOption.ReplaceExisting);

            await composition.RenderToFileAsync(outputFile,
                Windows.Media.Editing.MediaTrimmingPreference.Precise, profile);

            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public string? LastError { get; private set; }

    public void CleanupFrames()
    {
        if (_framesDir != null)
        {
            try { Directory.Delete(_framesDir, true); } catch { }
            _framesDir = null;
        }
    }

    private void CaptureFrame(object? sender, EventArgs e)
    {
        if (!_recording || _paused || _framesDir == null) return;

        if (Elapsed.TotalSeconds >= _maxSeconds)
        {
            Stop();
            OnMaxReached?.Invoke();
            return;
        }

        try
        {
            var hdc = NativeMethods.GetDC(IntPtr.Zero);
            var memDc = NativeMethods.CreateCompatibleDC(hdc);
            var hBmp = NativeMethods.CreateCompatibleBitmap(hdc, _regionW, _regionH);
            var old = NativeMethods.SelectObject(memDc, hBmp);

            NativeMethods.BitBlt(memDc, 0, 0, _regionW, _regionH, hdc,
                _regionX, _regionY, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);

            NativeMethods.SelectObject(memDc, old);

            using var bmp = DrawBitmap.FromHbitmap(hBmp);

            NativeMethods.DeleteObject(hBmp);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);

            var framePath = Path.Combine(_framesDir, $"frame_{_frameCount:D6}.jpg");
            bmp.Save(framePath, DrawImaging.ImageFormat.Jpeg);
            _frameCount++;
        }
        catch { }

        OnTick?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}

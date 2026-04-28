using System.IO;
using System.Windows.Threading;
using DrawBitmap = System.Drawing.Bitmap;
using DrawImaging = System.Drawing.Imaging;

namespace Llamashot.Core;

public class ScreenRecorder : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly int _fps;
    private int _regionX, _regionY, _regionW, _regionH;
    private DateTime _startTime;
    private TimeSpan _pausedElapsed;
    private bool _recording;
    private bool _paused;
    private bool _disposed;
    private string? _framesDir;
    private int _frameCount;

    // Audio recording via AudioGraph
    private Windows.Media.Audio.AudioGraph? _audioGraph;
    private Windows.Media.Audio.AudioDeviceInputNode? _audioMicNode;
    private Windows.Media.Audio.AudioDeviceInputNode? _audioLoopbackNode;
    private Windows.Media.Audio.AudioFileOutputNode? _audioOutputNode;
    private string? _audioFilePath;
    private bool _audioActive;

    public bool IsRecording => _recording;
    public bool IsPaused => _paused;
    public bool AudioActive => _audioActive;
    public bool HasMic { get; private set; }
    public bool HasSystemAudio { get; private set; }
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

    public ScreenRecorder(int fps = 10)
    {
        _fps = fps;
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

    public async Task<bool> StartAudioAsync()
    {
        if (_framesDir == null || !_recording) return false;

        try
        {
            var settings = new Windows.Media.Audio.AudioGraphSettings(
                Windows.Media.Render.AudioRenderCategory.Media)
            {
                QuantumSizeSelectionMode = Windows.Media.Audio.QuantumSizeSelectionMode.LowestLatency
            };

            var graphResult = await Windows.Media.Audio.AudioGraph.CreateAsync(settings);
            if (graphResult.Status != Windows.Media.Audio.AudioGraphCreationStatus.Success)
                return false;

            _audioGraph = graphResult.Graph;

            // Create file output node
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_framesDir);
            var audioFile = await folder.CreateFileAsync("audio.wav",
                Windows.Storage.CreationCollisionOption.ReplaceExisting);

            var wavProfile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(
                Windows.Media.MediaProperties.AudioEncodingQuality.Medium);

            var outputResult = await _audioGraph.CreateFileOutputNodeAsync(audioFile, wavProfile);
            if (outputResult.Status != Windows.Media.Audio.AudioFileNodeCreationStatus.Success)
            {
                _audioGraph.Dispose();
                _audioGraph = null;
                return false;
            }

            _audioOutputNode = outputResult.FileOutputNode;
            _audioFilePath = Path.Combine(_framesDir, "audio.wav");

            bool hasMic = false;
            bool hasLoopback = false;

            // Try microphone input
            try
            {
                var micResult = await _audioGraph.CreateDeviceInputNodeAsync(
                    Windows.Media.Capture.MediaCategory.Media);
                if (micResult.Status == Windows.Media.Audio.AudioDeviceNodeCreationStatus.Success)
                {
                    _audioMicNode = micResult.DeviceInputNode;
                    _audioMicNode.AddOutgoingConnection(_audioOutputNode);
                    hasMic = true;
                }
            }
            catch { }

            // Try system audio (loopback from render device)
            try
            {
                var renderId = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(
                    Windows.Media.Devices.AudioDeviceRole.Default);
                if (!string.IsNullOrEmpty(renderId))
                {
                    var renderDevice = await Windows.Devices.Enumeration.DeviceInformation
                        .CreateFromIdAsync(renderId);
                    var loopbackResult = await _audioGraph.CreateDeviceInputNodeAsync(
                        Windows.Media.Capture.MediaCategory.Media,
                        _audioGraph.EncodingProperties,
                        renderDevice);
                    if (loopbackResult.Status == Windows.Media.Audio.AudioDeviceNodeCreationStatus.Success)
                    {
                        _audioLoopbackNode = loopbackResult.DeviceInputNode;
                        _audioLoopbackNode.AddOutgoingConnection(_audioOutputNode);
                        hasLoopback = true;
                    }
                }
            }
            catch { }

            if (!hasMic && !hasLoopback)
            {
                // Neither worked — clean up
                _audioOutputNode = null;
                _audioGraph.Dispose();
                _audioGraph = null;
                _audioFilePath = null;
                return false;
            }

            HasMic = hasMic;
            HasSystemAudio = hasLoopback;
            _audioGraph.Start();
            _audioActive = true;
            return true;
        }
        catch
        {
            await StopAudioAsync();
            return false;
        }
    }

    public async Task StopAudioAsync()
    {
        if (_audioGraph == null && _audioOutputNode == null)
        {
            _audioActive = false;
            HasMic = false;
            HasSystemAudio = false;
            return;
        }

        _audioGraph?.Stop();

        if (_audioOutputNode != null)
        {
            try { await _audioOutputNode.FinalizeAsync(); } catch { }
            _audioOutputNode = null;
        }

        _audioMicNode?.Dispose();
        _audioMicNode = null;
        _audioLoopbackNode?.Dispose();
        _audioLoopbackNode = null;
        _audioGraph?.Dispose();
        _audioGraph = null;
        _audioActive = false;
        HasMic = false;
        HasSystemAudio = false;
    }

    public void Pause()
    {
        if (!_recording || _paused) return;
        _paused = true;
        _pausedElapsed += DateTime.Now - _startTime;
        _timer.Stop();
        _audioGraph?.Stop();
    }

    public void Resume()
    {
        if (!_recording || !_paused) return;
        _paused = false;
        _startTime = DateTime.Now;
        _timer.Start();
        if (_audioActive) _audioGraph?.Start();
    }

    public void Stop()
    {
        if (!_recording) return;
        if (!_paused)
            _pausedElapsed += DateTime.Now - _startTime;

        _recording = false;
        _paused = false;
        _timer.Stop();
        _audioGraph?.Stop();
    }

    public async Task<bool> SaveAsMp4Async(string outputPath)
    {
        if (_framesDir == null || _frameCount == 0) return false;

        try
        {
            // Finalize audio file before composing
            if (_audioOutputNode != null)
            {
                try { await _audioOutputNode.FinalizeAsync(); } catch { }
                _audioOutputNode = null;
            }

            // Dispose audio graph after finalizing
            _audioMicNode?.Dispose();
            _audioMicNode = null;
            _audioLoopbackNode?.Dispose();
            _audioLoopbackNode = null;
            _audioGraph?.Dispose();
            _audioGraph = null;
            _audioActive = false;

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

            // Add audio track if available
            if (_audioFilePath != null && File.Exists(_audioFilePath))
            {
                try
                {
                    var audioFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(_audioFilePath);
                    var audioTrack = await Windows.Media.Editing.BackgroundAudioTrack
                        .CreateFromFileAsync(audioFile);
                    composition.BackgroundAudioTracks.Add(audioTrack);
                }
                catch { }
            }

            var quality = _regionW >= 1920
                ? Windows.Media.MediaProperties.VideoEncodingQuality.HD1080p
                : _regionW >= 1280
                    ? Windows.Media.MediaProperties.VideoEncodingQuality.HD720p
                    : Windows.Media.MediaProperties.VideoEncodingQuality.Vga;

            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(quality);
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
        _audioFilePath = null;
    }

    private void CaptureFrame(object? sender, EventArgs e)
    {
        if (!_recording || _paused || _framesDir == null) return;

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
        _audioMicNode?.Dispose();
        _audioLoopbackNode?.Dispose();
        _audioGraph?.Dispose();
    }
}

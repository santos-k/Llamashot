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
    private TimeSpan _finalElapsed; // actual recording duration (minus pauses)

    // Audio
    private Windows.Media.Audio.AudioGraph? _audioGraph;
    private Windows.Media.Audio.AudioDeviceInputNode? _audioMicNode;
    private Windows.Media.Audio.AudioDeviceInputNode? _audioLoopbackNode;
    private Windows.Media.Audio.AudioFileOutputNode? _audioOutputNode;
    private string? _audioFilePath;

    public string? FramesDirectory => _framesDir;
    public bool IsRecording => _recording;
    public bool IsPaused => _paused;
    public bool HasMic { get; private set; }
    public bool HasSystemAudio { get; private set; }
    public bool MicActive { get; private set; }
    public bool SystemAudioActive { get; private set; }
    public bool AudioGraphRunning => _audioGraph != null;
    public int FramesCaptured => _frameCount;
    public string? LastError { get; private set; }

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

    // ============ AUDIO ============

    /// <summary>
    /// Creates audio graph with mic and system audio nodes. Both are created but
    /// individually started/stopped. Call once, then use SetMic/SetSystemAudio to toggle.
    /// </summary>
    public async Task<bool> InitAudioAsync(bool enableMic, bool enableSystemAudio)
    {
        if (_framesDir == null || !_recording || _audioGraph != null) return false;

        try
        {
            var settings = new Windows.Media.Audio.AudioGraphSettings(
                Windows.Media.Render.AudioRenderCategory.Media)
            {
                QuantumSizeSelectionMode = Windows.Media.Audio.QuantumSizeSelectionMode.LowestLatency
            };

            var graphResult = await Windows.Media.Audio.AudioGraph.CreateAsync(settings);
            if (graphResult.Status != Windows.Media.Audio.AudioGraphCreationStatus.Success)
            {
                LastError = $"AudioGraph creation failed: {graphResult.Status}";
                return false;
            }

            _audioGraph = graphResult.Graph;

            // Create output file
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(_framesDir);
            var audioFile = await folder.CreateFileAsync("audio.wav",
                Windows.Storage.CreationCollisionOption.ReplaceExisting);
            var wavProfile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(
                Windows.Media.MediaProperties.AudioEncodingQuality.Medium);
            var outputResult = await _audioGraph.CreateFileOutputNodeAsync(audioFile, wavProfile);
            if (outputResult.Status != Windows.Media.Audio.AudioFileNodeCreationStatus.Success)
            {
                LastError = $"Audio output node failed: {outputResult.Status}";
                _audioGraph.Dispose(); _audioGraph = null;
                return false;
            }
            _audioOutputNode = outputResult.FileOutputNode;
            _audioFilePath = Path.Combine(_framesDir, "audio.wav");

            // Try creating mic node
            try
            {
                var micResult = await _audioGraph.CreateDeviceInputNodeAsync(
                    Windows.Media.Capture.MediaCategory.Media);
                if (micResult.Status == Windows.Media.Audio.AudioDeviceNodeCreationStatus.Success)
                {
                    _audioMicNode = micResult.DeviceInputNode;
                    _audioMicNode.AddOutgoingConnection(_audioOutputNode);
                    HasMic = true;
                    if (!enableMic)
                    {
                        _audioMicNode.OutgoingGain = 0;
                        MicActive = false;
                    }
                    else
                    {
                        MicActive = true;
                    }
                }
            }
            catch { }

            // Try creating loopback node — try multiple methods
            try
            {
                Windows.Devices.Enumeration.DeviceInformation? renderDevice = null;

                // Method 1: Use audio render selector
                try
                {
                    var selector = Windows.Media.Devices.MediaDevice.GetAudioRenderSelector();
                    var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(selector);
                    if (devices.Count > 0)
                        renderDevice = devices[0];
                }
                catch { }

                // Method 2: Use default render device ID
                if (renderDevice == null)
                {
                    try
                    {
                        var renderId = Windows.Media.Devices.MediaDevice.GetDefaultAudioRenderId(
                            Windows.Media.Devices.AudioDeviceRole.Default);
                        if (!string.IsNullOrEmpty(renderId))
                            renderDevice = await Windows.Devices.Enumeration.DeviceInformation
                                .CreateFromIdAsync(renderId);
                    }
                    catch { }
                }

                if (renderDevice != null)
                {
                    var loopbackResult = await _audioGraph.CreateDeviceInputNodeAsync(
                        Windows.Media.Capture.MediaCategory.Media,
                        _audioGraph.EncodingProperties,
                        renderDevice);
                    if (loopbackResult.Status == Windows.Media.Audio.AudioDeviceNodeCreationStatus.Success)
                    {
                        _audioLoopbackNode = loopbackResult.DeviceInputNode;
                        _audioLoopbackNode.AddOutgoingConnection(_audioOutputNode);
                        HasSystemAudio = true;
                        if (!enableSystemAudio)
                        {
                            _audioLoopbackNode.OutgoingGain = 0;
                            SystemAudioActive = false;
                        }
                        else
                        {
                            SystemAudioActive = true;
                        }
                    }
                }
            }
            catch { }

            if (!HasMic && !HasSystemAudio)
            {
                _audioOutputNode = null;
                _audioGraph.Dispose(); _audioGraph = null;
                _audioFilePath = null;
                LastError = "No audio devices available";
                return false;
            }

            _audioGraph.Start();
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            try { _audioGraph?.Dispose(); } catch { }
            _audioGraph = null;
            return false;
        }
    }

    /// <summary>Toggle mic on/off via gain (no node creation/destruction).</summary>
    public void SetMicActive(bool active)
    {
        MicActive = active && HasMic;
        if (_audioMicNode != null)
            _audioMicNode.OutgoingGain = active ? 1.0 : 0.0;
    }

    /// <summary>Toggle system audio on/off via gain (no node creation/destruction).</summary>
    public void SetSystemAudioActive(bool active)
    {
        SystemAudioActive = active && HasSystemAudio;
        if (_audioLoopbackNode != null)
            _audioLoopbackNode.OutgoingGain = active ? 1.0 : 0.0;
    }

    public async Task StopAudioAsync()
    {
        try { _audioGraph?.Stop(); } catch { }

        if (_audioOutputNode != null)
        {
            try { await _audioOutputNode.FinalizeAsync(); } catch { }
            _audioOutputNode = null;
        }

        try { _audioMicNode?.Dispose(); } catch { } _audioMicNode = null;
        try { _audioLoopbackNode?.Dispose(); } catch { } _audioLoopbackNode = null;
        try { _audioGraph?.Dispose(); } catch { } _audioGraph = null;
        HasMic = false;
        HasSystemAudio = false;
        MicActive = false;
        SystemAudioActive = false;
    }

    // ============ RECORDING CONTROLS ============

    public void Pause()
    {
        if (!_recording || _paused) return;
        _paused = true;
        _pausedElapsed += DateTime.Now - _startTime;
        _timer.Stop();
        try { _audioGraph?.Stop(); } catch { }
    }

    public void Resume()
    {
        if (!_recording || !_paused) return;
        _paused = false;
        _startTime = DateTime.Now;
        _timer.Start();
        if (_audioGraph != null)
            try { _audioGraph.Start(); } catch { }
    }

    public void Stop()
    {
        if (!_recording) return;
        if (!_paused)
            _pausedElapsed += DateTime.Now - _startTime;
        _finalElapsed = _pausedElapsed;
        _recording = false;
        _paused = false;
        _timer.Stop();
        try { _audioGraph?.Stop(); } catch { }
    }

    // ============ SAVE ============

    /// <param name="outputPath">Output MP4 file path.</param>
    /// <param name="maxWidth">Max width in pixels (0 = original resolution).</param>
    /// <param name="progress">Progress callback (0.0 to 1.0).</param>
    public async Task<bool> SaveAsync(string outputPath, int maxWidth = 0, Action<double>? progress = null)
    {
        if (_framesDir == null || _frameCount == 0) return false;

        try
        {
            // Finalize audio
            if (_audioOutputNode != null)
            {
                try { await _audioOutputNode.FinalizeAsync(); } catch { }
                _audioOutputNode = null;
            }
            try { _audioMicNode?.Dispose(); } catch { } _audioMicNode = null;
            try { _audioLoopbackNode?.Dispose(); } catch { } _audioLoopbackNode = null;
            try { _audioGraph?.Dispose(); } catch { } _audioGraph = null;

            // Calculate output dimensions
            int outW = _regionW;
            int outH = _regionH;
            if (maxWidth > 0 && outW > maxWidth)
            {
                double scale = (double)maxWidth / outW;
                outW = maxWidth;
                outH = (int)(_regionH * scale);
            }
            outW = outW % 2 == 0 ? outW : outW - 1;
            outH = outH % 2 == 0 ? outH : outH - 1;

            double frameDurationMs = _finalElapsed.TotalSeconds > 0 && _frameCount > 0
                ? (_finalElapsed.TotalMilliseconds / _frameCount)
                : (1000.0 / _fps);

            var quality = outW >= 1920
                ? Windows.Media.MediaProperties.VideoEncodingQuality.HD1080p
                : outW >= 1280
                    ? Windows.Media.MediaProperties.VideoEncodingQuality.HD720p
                    : Windows.Media.MediaProperties.VideoEncodingQuality.Vga;

            var dir = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(dir);

            bool hasAudio = _audioFilePath != null && File.Exists(_audioFilePath);

            // Fast path: MediaStreamSource feeds decoded frames directly to H.264 encoder
            if (!hasAudio)
            {
                try
                {
                    bool fast = await SaveWithTranscoderAsync(outputPath, outW, outH,
                        frameDurationMs, quality, progress);
                    if (fast) return true;
                }
                catch { /* Fall through to composition path */ }
            }

            // Fallback: MediaComposition with individual image clips (slower but supports audio)
            var composition = new Windows.Media.Editing.MediaComposition();
            var clipDuration = TimeSpan.FromMilliseconds(frameDurationMs);
            for (int i = 0; i < _frameCount; i++)
            {
                var framePath = Path.Combine(_framesDir, $"frame_{i:D6}.jpg");
                if (!File.Exists(framePath)) continue;
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(framePath);
                var clip = await Windows.Media.Editing.MediaClip.CreateFromImageFileAsync(file, clipDuration);
                composition.Clips.Add(clip);
                progress?.Invoke((double)i / _frameCount * 0.5);
            }

            if (composition.Clips.Count == 0) return false;

            if (hasAudio)
            {
                try
                {
                    var audioFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(_audioFilePath!);
                    var audioTrack = await Windows.Media.Editing.BackgroundAudioTrack
                        .CreateFromFileAsync(audioFile);
                    composition.BackgroundAudioTracks.Add(audioTrack);
                }
                catch { }
            }

            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(quality);
            profile.Video.Width = (uint)outW;
            profile.Video.Height = (uint)outH;

            var outputFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(dir);
            var outputFile = await outputFolder.CreateFileAsync(
                Path.GetFileName(outputPath), Windows.Storage.CreationCollisionOption.ReplaceExisting);

            var renderOp = composition.RenderToFileAsync(outputFile,
                Windows.Media.Editing.MediaTrimmingPreference.Precise, profile);
            if (progress != null)
                renderOp.Progress = (info, p) => progress(0.5 + p / 200.0);

            var result = await renderOp;
            return result == Windows.Media.Transcoding.TranscodeFailureReason.None;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Fast save: uses MediaStreamSource to feed decoded BGRA frames directly
    /// to MediaTranscoder's H.264 encoder. No intermediate file needed.
    /// </summary>
    private async Task<bool> SaveWithTranscoderAsync(string outputPath, int outW, int outH,
        double frameDurationMs, Windows.Media.MediaProperties.VideoEncodingQuality quality,
        Action<double>? progress)
    {
        var videoProps = Windows.Media.MediaProperties.VideoEncodingProperties.CreateUncompressed(
            Windows.Media.MediaProperties.MediaEncodingSubtypes.Bgra8,
            (uint)_regionW, (uint)_regionH);
        uint fpsX1000 = (uint)Math.Round(1000000.0 / frameDurationMs);
        videoProps.FrameRate.Numerator = fpsX1000;
        videoProps.FrameRate.Denominator = 1000;

        var videoDesc = new Windows.Media.Core.VideoStreamDescriptor(videoProps);
        var mss = new Windows.Media.Core.MediaStreamSource(videoDesc);
        mss.Duration = TimeSpan.FromMilliseconds(_frameCount * frameDurationMs);
        mss.CanSeek = false;
        mss.BufferTime = TimeSpan.Zero;

        int frameIndex = 0;
        string framesDir = _framesDir!;
        int totalFrames = _frameCount;

        mss.SampleRequested += (sender, args) =>
        {
            if (frameIndex >= totalFrames)
            {
                args.Request.Sample = null;
                return;
            }

            var path = Path.Combine(framesDir, $"frame_{frameIndex:D6}.jpg");
            if (!File.Exists(path))
            {
                args.Request.Sample = null;
                return;
            }

            // Decode JPEG to raw BGRA pixels
            using var bmp = new DrawBitmap(path);
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, DrawImaging.ImageLockMode.ReadOnly,
                DrawImaging.PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[bmpData.Stride * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(bmpData);

            var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels);
            var timestamp = TimeSpan.FromMilliseconds(frameIndex * frameDurationMs);
            var sample = Windows.Media.Core.MediaStreamSample.CreateFromBuffer(buffer, timestamp);
            sample.Duration = TimeSpan.FromMilliseconds(frameDurationMs);
            args.Request.Sample = sample;

            frameIndex++;
            progress?.Invoke((double)frameIndex / totalFrames);
        };

        var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(quality);
        profile.Video.Width = (uint)outW;
        profile.Video.Height = (uint)outH;
        profile.Audio = null;

        var dir = Path.GetDirectoryName(outputPath)!;
        var outputFolder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(dir);
        var outputFile = await outputFolder.CreateFileAsync(
            Path.GetFileName(outputPath), Windows.Storage.CreationCollisionOption.ReplaceExisting);

        using var outputStream = await outputFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);

        var transcoder = new Windows.Media.Transcoding.MediaTranscoder();
        var prepResult = await transcoder.PrepareMediaStreamSourceTranscodeAsync(
            mss, outputStream, profile);

        if (!prepResult.CanTranscode)
        {
            LastError = $"Transcode failed: {prepResult.FailureReason}";
            return false;
        }

        await prepResult.TranscodeAsync();
        return true;
    }

    public void CleanupFrames()
    {
        if (_framesDir != null)
        {
            try { Directory.Delete(_framesDir, true); } catch { }
            _framesDir = null;
        }
        _audioFilePath = null;
    }

    // ============ FRAME CAPTURE ============

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
        try { _audioMicNode?.Dispose(); } catch { }
        try { _audioLoopbackNode?.Dispose(); } catch { }
        try { _audioGraph?.Dispose(); } catch { }
    }
}

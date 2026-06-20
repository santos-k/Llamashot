using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Llamashot.Core;

/// <summary>
/// AI subject cut-out using the U²-Net salient-object model via ONNX Runtime. The model
/// (~168 MB) is downloaded to %LOCALAPPDATA%\Llamashot\models on first use, then cached.
/// Produces a soft alpha matte for any photo — including complex natural backgrounds that
/// the colour-based <see cref="BackgroundRemover"/> cannot segment.
/// </summary>
public static class AiMatting
{
    private const string ModelUrl = "https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx";
    private const int N = 320; // U²-Net input size

    public static string ModelPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Llamashot", "models", "u2net.onnx");

    public static bool ModelExists => File.Exists(ModelPath);

    /// <summary>Downloads the model if missing. <paramref name="progress"/> reports 0–100.</summary>
    public static async Task EnsureModelAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (ModelExists) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ModelPath)!);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        using var resp = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? -1;

        string tmp = ModelPath + ".part";
        await using (var src = await resp.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tmp))
        {
            var buf = new byte[81920];
            long read = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report(read * 100.0 / total);
            }
        }
        File.Move(tmp, ModelPath, overwrite: true);
    }

    private static InferenceSession? _session;
    private static InferenceSession GetSession() => _session ??= new InferenceSession(ModelPath);

    /// <summary>
    /// Runs the model on a BGRA buffer and returns a per-pixel alpha matte (0–255), length w*h.
    /// CPU-bound — call from a background thread.
    /// </summary>
    public static byte[] ComputeMask(byte[] bgra, int w, int h)
    {
        var session = GetSession();

        // Preprocess: bilinear-free nearest downscale to N×N, RGB, ImageNet normalise.
        float[] mean = { 0.485f, 0.456f, 0.406f }, std = { 0.229f, 0.224f, 0.225f };
        var input = new DenseTensor<float>(new[] { 1, 3, N, N });
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int sx = x * w / N, sy = y * h / N;
                int i = (sy * w + sx) * 4;
                float b = bgra[i] / 255f, g = bgra[i + 1] / 255f, r = bgra[i + 2] / 255f;
                input[0, 0, y, x] = (r - mean[0]) / std[0];
                input[0, 1, y, x] = (g - mean[1]) / std[1];
                input[0, 2, y, x] = (b - mean[2]) / std[2];
            }

        string inName = session.InputMetadata.Keys.First();
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inName, input) });
        float[] outArr = results.First().AsTensor<float>().ToArray(); // [1,1,N,N] → N*N

        float mi = float.MaxValue, ma = float.MinValue;
        for (int k = 0; k < outArr.Length; k++) { float v = outArr[k]; if (v < mi) mi = v; if (v > ma) ma = v; }
        float range = Math.Max(1e-6f, ma - mi);

        var mask = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int mx = x * N / w, my = y * N / h;
                float v = (outArr[my * N + mx] - mi) / range;
                mask[y * w + x] = (byte)Math.Clamp((int)(v * 255f), 0, 255);
            }
        return mask;
    }

    /// <summary>Applies a matte as the image's alpha. Values below <paramref name="cutoff"/> become fully
    /// transparent and a small band above it ramps to opaque, giving a clean but soft edge.</summary>
    public static void ApplyMask(byte[] bgra, byte[] mask, int cutoff = 40, int rampTo = 160)
    {
        double span = Math.Max(1, rampTo - cutoff);
        for (int p = 0; p < mask.Length; p++)
        {
            int m = mask[p];
            int a = m <= cutoff ? 0 : m >= rampTo ? 255 : (int)((m - cutoff) / span * 255);
            bgra[p * 4 + 3] = (byte)a;
        }
    }
}

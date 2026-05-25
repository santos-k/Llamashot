using System.IO;
using DrawBitmap = System.Drawing.Bitmap;
using DrawImaging = System.Drawing.Imaging;

namespace Llamashot.Core;

/// <summary>
/// Encodes captured JPEG frames into an animated GIF (GIF89a).
/// Uses System.Drawing for color quantization + LZW, then assembles
/// the binary animated-GIF format with per-frame local color tables.
/// </summary>
public static class GifEncoder
{
    public const int MaxGifSeconds = 30;

    /// <summary>
    /// Creates an animated GIF from frame files on disk.
    /// </summary>
    /// <param name="framesDir">Directory containing frame_NNNNNN.jpg files.</param>
    /// <param name="frameCount">Total number of frames captured.</param>
    /// <param name="fps">Frames per second of the recording.</param>
    /// <param name="outputPath">Output .gif file path.</param>
    /// <param name="maxWidth">Max width in pixels (0 = original resolution).</param>
    /// <param name="frameSkip">Frame skip factor (1 = all frames, 2 = every other frame, etc.).</param>
    /// <param name="progress">Optional callback receiving 0.0–1.0 progress.</param>
    public static async Task<bool> SaveAsync(string framesDir, int frameCount, int fps,
        string outputPath, int maxWidth = 0, int frameSkip = 1, Action<double>? progress = null)
    {
        int maxFrames = MaxGifSeconds * fps;
        int totalFrames = Math.Min(frameCount, maxFrames);
        if (totalFrames == 0) return false;
        frameSkip = Math.Max(1, frameSkip);

        return await Task.Run(() =>
        {
            try
            {
                int delay = Math.Max(2, (100 / fps) * frameSkip); // centiseconds per frame

                // Determine target dimensions from first frame
                int w, h;
                var firstPath = Path.Combine(framesDir, "frame_000000.jpg");
                if (!File.Exists(firstPath)) return false;

                using (var first = new DrawBitmap(firstPath))
                {
                    if (maxWidth > 0 && first.Width > maxWidth)
                    {
                        double scale = (double)maxWidth / first.Width;
                        w = Math.Max(1, (int)(first.Width * scale));
                        h = Math.Max(1, (int)(first.Height * scale));
                    }
                    else
                    {
                        w = first.Width;
                        h = first.Height;
                    }
                }

                using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

                // === GIF89a Header ===
                output.Write("GIF89a"u8);

                // === Logical Screen Descriptor (7 bytes) ===
                WriteUInt16(output, (ushort)w);
                WriteUInt16(output, (ushort)h);
                output.WriteByte(0x70); // No GCT, color resolution = 8 bits
                output.WriteByte(0);    // Background color index
                output.WriteByte(0);    // Pixel aspect ratio

                // === NETSCAPE2.0 Application Extension (infinite loop) ===
                output.Write([0x21, 0xFF, 0x0B]);
                output.Write("NETSCAPE2.0"u8);
                output.Write([0x03, 0x01]);
                WriteUInt16(output, 0); // Loop count 0 = infinite
                output.WriteByte(0);    // Block terminator

                int outputFrameCount = (totalFrames + frameSkip - 1) / frameSkip;
                int written = 0;

                for (int i = 0; i < totalFrames; i += frameSkip)
                {
                    var framePath = Path.Combine(framesDir, $"frame_{i:D6}.jpg");
                    if (!File.Exists(framePath)) continue;

                    // Load, resize, and encode single frame as GIF
                    byte[] frameGif;
                    using (var bmp = new DrawBitmap(framePath))
                    using (var resized = new DrawBitmap(bmp, w, h))
                    using (var ms = new MemoryStream())
                    {
                        resized.Save(ms, DrawImaging.ImageFormat.Gif);
                        frameGif = ms.ToArray();
                    }

                    // Parse single-frame GIF to extract color table + LZW data
                    if (!WriteFrame(output, frameGif, (ushort)w, (ushort)h, (ushort)delay))
                        continue;

                    written++;
                    progress?.Invoke((double)written / outputFrameCount);
                }

                // === GIF Trailer ===
                output.WriteByte(0x3B);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Extracts color table and LZW image data from a single-frame GIF
    /// produced by System.Drawing, then writes it as an animated frame
    /// (Graphic Control Extension + Image Descriptor + Local Color Table + Image Data).
    /// </summary>
    private static bool WriteFrame(FileStream output, byte[] gif, ushort w, ushort h, ushort delay)
    {
        if (gif.Length < 14) return false;

        // Parse LSD packed byte (byte 10)
        byte packed = gif[10];
        bool hasGct = (packed & 0x80) != 0;
        int gctBits = packed & 0x07;
        int gctEntries = hasGct ? (1 << (gctBits + 1)) : 0;
        int gctBytes = gctEntries * 3;

        // Extract global color table
        byte[] colorTable = new byte[gctBytes];
        if (hasGct && gif.Length >= 13 + gctBytes)
            Array.Copy(gif, 13, colorTable, 0, gctBytes);

        // Scan past header (6) + LSD (7) + GCT
        int pos = 13 + gctBytes;

        // Skip any extension blocks to find Image Descriptor (0x2C)
        while (pos < gif.Length)
        {
            if (gif[pos] == 0x2C) break;
            if (gif[pos] == 0x21) // Extension block
            {
                pos += 2; // Introducer + label
                while (pos < gif.Length)
                {
                    int blockLen = gif[pos];
                    pos++;
                    if (blockLen == 0) break;
                    pos += blockLen;
                }
            }
            else if (gif[pos] == 0x3B) return false; // Trailer — no image data
            else pos++;
        }

        if (pos >= gif.Length || gif[pos] != 0x2C) return false;

        // === Graphic Control Extension ===
        output.Write([0x21, 0xF9, 0x04]);
        output.WriteByte(0x04); // Disposal=1 (do not dispose), no transparency
        WriteUInt16(output, delay);
        output.WriteByte(0);    // Transparent color index
        output.WriteByte(0);    // Block terminator

        // === Image Descriptor ===
        output.WriteByte(0x2C);
        WriteUInt16(output, 0); // Left
        WriteUInt16(output, 0); // Top
        WriteUInt16(output, w);
        WriteUInt16(output, h);

        // Local color table flag + size
        if (hasGct && gctBytes > 0)
        {
            output.WriteByte((byte)(0x80 | gctBits));
            output.Write(colorTable);
        }
        else
        {
            output.WriteByte(0x00);
        }

        // Skip past Image Descriptor in source: 0x2C(1) + left(2) + top(2) + w(2) + h(2) + packed(1) = 10
        pos++; // skip 0x2C
        pos += 8; // skip left, top, width, height
        byte imgPacked = gif[pos];
        pos++; // skip packed byte

        // Skip source's local color table if present
        if ((imgPacked & 0x80) != 0)
        {
            int lctBits = imgPacked & 0x07;
            int lctSize = 3 * (1 << (lctBits + 1));
            pos += lctSize;
        }

        // === LZW Image Data ===
        if (pos >= gif.Length) return false;

        // LZW minimum code size
        output.WriteByte(gif[pos]);
        pos++;

        // Data sub-blocks
        while (pos < gif.Length)
        {
            int blockSize = gif[pos];
            if (blockSize == 0)
            {
                output.WriteByte(0); // Block terminator
                break;
            }
            if (pos + blockSize + 1 > gif.Length) break;
            output.Write(gif, pos, blockSize + 1);
            pos += blockSize + 1;
        }

        return true;
    }

    private static void WriteUInt16(FileStream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)(value >> 8));
    }
}

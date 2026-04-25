using System.IO;
using System.Text;

namespace Llamashot.Core;

/// <summary>
/// Minimal MJPEG AVI writer. Each frame is a JPEG image.
/// </summary>
public class AviWriter : IDisposable
{
    private readonly BinaryWriter _writer;
    private readonly int _width, _height, _fps;
    private readonly List<(long offset, int size)> _frameIndex = new();
    private long _moviStart;
    private bool _disposed;

    public int FrameCount => _frameIndex.Count;

    public AviWriter(Stream output, int width, int height, int fps)
    {
        _writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: false);
        _width = width;
        _height = height;
        _fps = fps;
        WriteHeaders();
    }

    private void WriteHeaders()
    {
        // RIFF header (size patched on close)
        WriteChunkId("RIFF");
        _writer.Write(0); // placeholder for file size
        WriteChunkId("AVI ");

        // LIST hdrl
        WriteChunkId("LIST");
        long hdrlSizePos = _writer.BaseStream.Position;
        _writer.Write(0); // placeholder
        WriteChunkId("hdrl");

        // avih - main AVI header
        WriteChunkId("avih");
        _writer.Write(56); // size of avih data
        _writer.Write(1000000 / _fps); // microseconds per frame
        _writer.Write(0); // max bytes per sec (0 = unknown)
        _writer.Write(0); // padding
        _writer.Write(0x10); // flags: AVIF_HASINDEX
        _writer.Write(0); // total frames (patched on close)
        _writer.Write(0); // initial frames
        _writer.Write(1); // streams
        _writer.Write(0); // suggested buffer size
        _writer.Write(_width);
        _writer.Write(_height);
        _writer.Write(0); _writer.Write(0); _writer.Write(0); _writer.Write(0); // reserved

        // LIST strl
        WriteChunkId("LIST");
        long strlSizePos = _writer.BaseStream.Position;
        _writer.Write(0); // placeholder
        WriteChunkId("strl");

        // strh - stream header
        WriteChunkId("strh");
        _writer.Write(64); // size
        WriteChunkId("vids"); // type
        WriteChunkId("MJPG"); // codec
        _writer.Write(0); // flags
        _writer.Write((short)0); // priority
        _writer.Write((short)0); // language
        _writer.Write(0); // initial frames
        _writer.Write(1); // scale
        _writer.Write(_fps); // rate
        _writer.Write(0); // start
        _writer.Write(0); // length (patched on close)
        _writer.Write(0); // suggested buffer size
        _writer.Write(-1); // quality
        _writer.Write(0); // sample size
        _writer.Write((short)0); _writer.Write((short)0); // frame rect
        _writer.Write((short)_width); _writer.Write((short)_height);

        // strf - stream format (BITMAPINFOHEADER)
        WriteChunkId("strf");
        _writer.Write(40); // size of BITMAPINFOHEADER
        _writer.Write(40); // biSize
        _writer.Write(_width);
        _writer.Write(_height);
        _writer.Write((short)1); // planes
        _writer.Write((short)24); // bpp
        WriteFourCC("MJPG"); // compression
        _writer.Write(_width * _height * 3); // image size
        _writer.Write(0); _writer.Write(0); // ppm
        _writer.Write(0); _writer.Write(0); // colors

        // Patch strl size
        long strlEnd = _writer.BaseStream.Position;
        _writer.BaseStream.Position = strlSizePos;
        _writer.Write((int)(strlEnd - strlSizePos - 4));
        _writer.BaseStream.Position = strlEnd;

        // Patch hdrl size
        long hdrlEnd = _writer.BaseStream.Position;
        _writer.BaseStream.Position = hdrlSizePos;
        _writer.Write((int)(hdrlEnd - hdrlSizePos - 4));
        _writer.BaseStream.Position = hdrlEnd;

        // LIST movi
        WriteChunkId("LIST");
        _writer.Write(0); // placeholder (patched on close)
        WriteChunkId("movi");
        _moviStart = _writer.BaseStream.Position;
    }

    public void AddFrame(byte[] jpegData)
    {
        long offset = _writer.BaseStream.Position - _moviStart + 4;
        WriteChunkId("00dc");
        _writer.Write(jpegData.Length);
        _writer.Write(jpegData);

        // Pad to word boundary
        if (jpegData.Length % 2 != 0)
            _writer.Write((byte)0);

        _frameIndex.Add((offset, jpegData.Length));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Patch movi LIST size
        long moviEnd = _writer.BaseStream.Position;
        _writer.BaseStream.Position = _moviStart - 8;
        _writer.Write((int)(moviEnd - _moviStart + 4));
        _writer.BaseStream.Position = moviEnd;

        // Write idx1 index
        WriteChunkId("idx1");
        _writer.Write(_frameIndex.Count * 16);
        foreach (var (offset, size) in _frameIndex)
        {
            WriteChunkId("00dc");
            _writer.Write(0x10); // AVIIF_KEYFRAME
            _writer.Write((int)offset);
            _writer.Write(size);
        }

        // Patch total file size
        long fileEnd = _writer.BaseStream.Position;
        _writer.BaseStream.Position = 4;
        _writer.Write((int)(fileEnd - 8));

        // Patch total frames in avih
        _writer.BaseStream.Position = 48;
        _writer.Write(_frameIndex.Count);

        // Patch length in strh
        _writer.BaseStream.Position = 140;
        _writer.Write(_frameIndex.Count);

        _writer.Flush();
        _writer.Dispose();
    }

    private void WriteChunkId(string id)
    {
        _writer.Write(Encoding.ASCII.GetBytes(id.PadRight(4).Substring(0, 4)));
    }

    private void WriteFourCC(string fourcc)
    {
        _writer.Write(Encoding.ASCII.GetBytes(fourcc.PadRight(4).Substring(0, 4)));
    }
}

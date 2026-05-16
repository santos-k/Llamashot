using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Llamashot.Core;

/// <summary>
/// Stitches multiple overlapping screenshot frames into a single long image.
/// Detects and removes fixed headers/footers to avoid duplication.
/// </summary>
public static class ImageStitcher
{
    private const int MatchTolerance = 4;
    private const int SampleStep = 2;
    private const int MinOverlap = 8;

    public static BitmapSource? Stitch(List<BitmapSource> frames)
    {
        if (frames.Count == 0) return null;
        if (frames.Count == 1) return frames[0];

        int width = frames[0].PixelWidth;
        int frameH = frames[0].PixelHeight;

        // Detect the changing region by comparing first two frames.
        int fixedTopRows = 0, fixedBottomRows = 0;
        if (frames.Count >= 2)
        {
            var p0 = GetPixels(frames[0]);
            var p1 = GetPixels(frames[1]);
            (fixedTopRows, fixedBottomRows) = DetectChangingRegion(p0, p1, width, frameH);
        }

        int contentHeight = frameH - fixedTopRows - fixedBottomRows;

        // Build result: start with first frame (header + first content, no footer)
        var resultPixels = GetPixels(frames[0]);
        int resultHeight = frameH;

        if (fixedBottomRows > 0)
        {
            resultHeight -= fixedBottomRows;
            resultPixels = CropPixels(resultPixels, width, frameH, 0, resultHeight);
        }

        int lastGoodOverlap = 0; // Track last successful overlap for fallback

        for (int i = 1; i < frames.Count; i++)
        {
            var framePixels = GetPixels(frames[i]);
            int fH = frames[i].PixelHeight;

            int contentTop = fixedTopRows;
            int contentBottom = fH - fixedBottomRows;
            int cHeight = contentBottom - contentTop;
            if (cHeight <= MinOverlap) continue;

            // Find overlap: try forward search (bottom of result → top of frame),
            // then reverse search (top of frame → bottom of result) as fallback
            int overlap = FindOverlapMultiRef(resultPixels, width, resultHeight,
                                               framePixels, width, fH,
                                               contentTop, contentBottom);

            if (overlap < MinOverlap)
            {
                // Reverse search: take reference rows from top of new frame's content,
                // find them in the bottom of the result. Handles large scroll distances.
                overlap = FindOverlapReverse(resultPixels, width, resultHeight,
                                              framePixels, width, fH,
                                              contentTop, contentBottom);
            }

            if (overlap >= MinOverlap)
            {
                lastGoodOverlap = overlap;
            }
            else if (lastGoodOverlap > 0)
            {
                // Reuse last known good overlap
                overlap = lastGoodOverlap;
            }
            else
            {
                // No overlap found at all — skip this frame rather than blindly appending
                // which causes duplication of headers/sidebars
                continue;
            }

            int newContentStart = contentTop + overlap;
            int newContentEnd = (i == frames.Count - 1) ? fH : contentBottom;
            int newRows = newContentEnd - newContentStart;

            if (newRows <= 0) continue;

            var newContentPixels = CropPixels(framePixels, width, fH, newContentStart, newRows);
            resultPixels = AppendPixels(resultPixels, width, resultHeight, newContentPixels, width, newRows);
            resultHeight += newRows;
        }

        return CreateBitmap(resultPixels, width, resultHeight);
    }

    /// <summary>
    /// Detect the changing region between two frames by scanning from top and bottom.
    /// No artificial cap — finds the true boundary of fixed headers/footers.
    /// </summary>
    private static (int fixedTop, int fixedBottom) DetectChangingRegion(
        byte[] pixels1, byte[] pixels2, int width, int height)
    {
        int stride = width * 4;

        int fixedTop = 0;
        for (int y = 0; y < height; y++)
        {
            if (!RowsMatchExact(pixels1, pixels2, y, stride))
                break;
            fixedTop++;
        }

        int fixedBottom = 0;
        for (int y = height - 1; y > fixedTop; y--)
        {
            if (!RowsMatchExact(pixels1, pixels2, y, stride))
                break;
            fixedBottom++;
        }

        // Sanity: keep at least 15% as content area
        int minContent = height / 7;
        if (fixedTop + fixedBottom > height - minContent)
        {
            int excess = fixedTop + fixedBottom - (height - minContent);
            int reduceBottom = Math.Min(excess, fixedBottom);
            fixedBottom -= reduceBottom;
            excess -= reduceBottom;
            fixedTop -= excess;
        }

        return (Math.Max(0, fixedTop), Math.Max(0, fixedBottom));
    }

    private static bool RowsMatchExact(byte[] pixels1, byte[] pixels2, int row, int stride)
    {
        int offset = row * stride;
        for (int x = 0; x < stride; x += SampleStep * 4)
        {
            int idx = offset + x;
            if (idx + 2 >= pixels1.Length || idx + 2 >= pixels2.Length) return false;
            if (Math.Abs(pixels1[idx] - pixels2[idx]) > MatchTolerance ||
                Math.Abs(pixels1[idx + 1] - pixels2[idx + 1]) > MatchTolerance ||
                Math.Abs(pixels1[idx + 2] - pixels2[idx + 2]) > MatchTolerance)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Try multiple reference rows to find the overlap. This handles cases where a single
    /// reference row might land on a repeated pattern or animated content.
    /// </summary>
    private static int FindOverlapMultiRef(
        byte[] resultPixels, int resultW, int resultH,
        byte[] framePixels, int frameW, int frameH,
        int contentTop, int contentBottom)
    {
        int stride = resultW * 4;
        int contentHeight = contentBottom - contentTop;

        // Try reference rows at different offsets from the bottom of the result
        // Include small offsets (0-8) for tiny overlaps when Page Down scrolls most of the viewport
        int[] refOffsets = { 0, 2, 5, 8, 10, 15, 20, 25, 35, 50, 70 };

        foreach (int refOffset in refOffsets)
        {
            if (refOffset >= resultH || refOffset >= contentHeight / 2) continue;

            int refRowInResult = resultH - 1 - refOffset;
            if (refRowInResult < 0) continue;

            long refHash = ComputeRowHash(resultPixels, refRowInResult, stride);

            // Search for this row in the new frame's content area
            for (int y = contentTop; y < contentBottom - refOffset; y++)
            {
                long frameHash = ComputeRowHash(framePixels, y, stride);
                if (frameHash != refHash) continue;

                if (!RowsMatch(resultPixels, refRowInResult, framePixels, y, stride))
                    continue;

                // Verify by checking multiple surrounding rows
                int verified = VerifyMatch(resultPixels, resultW, resultH,
                                            framePixels, frameW, frameH,
                                            refRowInResult, y, contentTop, contentBottom);
                if (verified >= 4)
                {
                    int overlap = y - contentTop + refOffset + 1;
                    if (overlap >= MinOverlap && overlap < contentHeight)
                        return overlap;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Reverse search: take reference rows from near the TOP of the new frame's content area,
    /// and search for them in the BOTTOM of the accumulated result.
    /// This handles cases where the scroll was so large that the forward search
    /// (bottom of result → top of frame) finds no match.
    /// </summary>
    private static int FindOverlapReverse(
        byte[] resultPixels, int resultW, int resultH,
        byte[] framePixels, int frameW, int frameH,
        int contentTop, int contentBottom)
    {
        int stride = resultW * 4;
        int contentHeight = contentBottom - contentTop;

        // Try reference rows near the top of the frame's content area
        int[] refOffsets = { 2, 5, 10, 15, 20, 30, 50 };

        foreach (int refOffset in refOffsets)
        {
            int refRowInFrame = contentTop + refOffset;
            if (refRowInFrame >= contentBottom) continue;

            long refHash = ComputeRowHash(framePixels, refRowInFrame, stride);

            // Search in the bottom portion of the result (wider range for accumulated results)
            int searchStart = Math.Max(0, resultH - contentHeight * 2);
            for (int y = resultH - 1; y >= searchStart; y--)
            {
                long resultHash = ComputeRowHash(resultPixels, y, stride);
                if (resultHash != refHash) continue;

                if (!RowsMatch(framePixels, refRowInFrame, resultPixels, y, stride))
                    continue;

                int verified = 0;
                for (int d = -3; d <= 3; d++)
                {
                    int fr = refRowInFrame + d;
                    int rr = y + d;
                    if (fr < contentTop || fr >= contentBottom || rr < 0 || rr >= resultH) continue;
                    if (RowsMatch(framePixels, fr, resultPixels, rr, stride))
                        verified++;
                }

                if (verified >= 4)
                {
                    // The overlap: the frame's row (contentTop + refOffset) matches result row y.
                    // So frame rows contentTop..contentTop+refOffset overlap with result rows (y-refOffset)..y
                    // New content starts at the row AFTER the last overlapping row in the frame.
                    int overlapRows = resultH - (y - refOffset);
                    if (overlapRows >= MinOverlap && overlapRows < contentHeight)
                        return overlapRows;
                }
            }
        }
        return 0;
    }

    private static long ComputeRowHash(byte[] pixels, int row, int stride)
    {
        long hash = 0;
        int offset = row * stride;
        for (int x = 0; x < stride; x += SampleStep * 4)
        {
            int idx = offset + x;
            if (idx + 2 >= pixels.Length) break;
            int b = pixels[idx] / (MatchTolerance + 1);
            int g = pixels[idx + 1] / (MatchTolerance + 1);
            int r = pixels[idx + 2] / (MatchTolerance + 1);
            hash = hash * 31 + b + g * 256 + r * 65536;
        }
        return hash;
    }

    private static bool RowsMatch(byte[] pixels1, int row1, byte[] pixels2, int row2, int stride)
    {
        int o1 = row1 * stride;
        int o2 = row2 * stride;
        int mismatches = 0;
        int total = 0;

        for (int x = 0; x < stride; x += SampleStep * 4)
        {
            int i1 = o1 + x;
            int i2 = o2 + x;
            if (i1 + 2 >= pixels1.Length || i2 + 2 >= pixels2.Length) break;
            total++;
            if (Math.Abs(pixels1[i1] - pixels2[i2]) > MatchTolerance ||
                Math.Abs(pixels1[i1 + 1] - pixels2[i2 + 1]) > MatchTolerance ||
                Math.Abs(pixels1[i1 + 2] - pixels2[i2 + 2]) > MatchTolerance)
                mismatches++;
        }
        // Allow up to 3% mismatched pixels
        return total > 0 && mismatches <= Math.Max(1, total / 33);
    }

    private static int VerifyMatch(byte[] resultPixels, int resultW, int resultH,
                                    byte[] framePixels, int frameW, int frameH,
                                    int resultRow, int frameRow,
                                    int contentTop, int contentBottom)
    {
        int stride = resultW * 4;
        int verified = 0;
        // Check 11 surrounding rows
        for (int d = -5; d <= 5; d++)
        {
            int rr = resultRow + d;
            int fr = frameRow + d;
            if (rr < 0 || rr >= resultH || fr < contentTop || fr >= contentBottom) continue;
            if (RowsMatch(resultPixels, rr, framePixels, fr, stride))
                verified++;
        }
        return verified;
    }

    private static byte[] GetPixels(BitmapSource source)
    {
        var formatted = source;
        if (source.Format != PixelFormats.Bgra32)
        {
            formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            formatted.Freeze();
        }
        int stride = formatted.PixelWidth * 4;
        var pixels = new byte[stride * formatted.PixelHeight];
        formatted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static byte[] CropPixels(byte[] pixels, int width, int height, int startRow, int rowCount)
    {
        int stride = width * 4;
        int srcOffset = startRow * stride;
        int length = rowCount * stride;
        if (srcOffset + length > pixels.Length) length = pixels.Length - srcOffset;
        var result = new byte[length];
        Buffer.BlockCopy(pixels, srcOffset, result, 0, length);
        return result;
    }

    private static byte[] AppendPixels(byte[] existing, int existingW, int existingH,
                                        byte[] newPixels, int newW, int newH)
    {
        int existingLen = existingW * 4 * existingH;
        int newLen = newW * 4 * newH;
        var result = new byte[existingLen + newLen];
        Buffer.BlockCopy(existing, 0, result, 0, Math.Min(existingLen, existing.Length));
        Buffer.BlockCopy(newPixels, 0, result, existingLen, Math.Min(newLen, newPixels.Length));
        return result;
    }

    private static BitmapSource CreateBitmap(byte[] pixels, int width, int height)
    {
        int stride = width * 4;
        var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>
    /// Check if two frames are effectively identical (98%+ pixels match).
    /// Tolerates minor differences from animations, cursor blink, scrollbar fade.
    /// </summary>
    public static bool AreFramesIdentical(BitmapSource a, BitmapSource b)
    {
        if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) return false;
        var pa = GetPixels(a);
        var pb = GetPixels(b);
        int stride = a.PixelWidth * 4;
        int total = 0;
        int mismatches = 0;

        for (int y = 0; y < a.PixelHeight; y += 3)
        {
            int offset = y * stride;
            for (int x = 0; x < stride; x += SampleStep * 4)
            {
                int idx = offset + x;
                if (idx + 2 >= pa.Length) break;
                total++;
                if (Math.Abs(pa[idx] - pb[idx]) > MatchTolerance ||
                    Math.Abs(pa[idx + 1] - pb[idx + 1]) > MatchTolerance ||
                    Math.Abs(pa[idx + 2] - pb[idx + 2]) > MatchTolerance)
                    mismatches++;
            }
        }

        // 98% match = identical (allows 2% for animations, cursor, ads)
        return total > 0 && mismatches <= total / 50;
    }
}

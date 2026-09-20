using SkiaSharp;

namespace LuminaIDE;

/// <summary>All the frames of an animated GIF, fully composed (each one is a complete picture), as premultiplied BGRA.</summary>
public sealed class GifFrames
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required byte[][] Bgra { get; init; }
    /// <summary>Per-frame display time in milliseconds, already normalised (see <see cref="GifDecoder.NormalizeDelay"/>).</summary>
    public required int[] DelaysMs { get; init; }
    /// <summary>-1 = loop forever; otherwise how many times to repeat after the first play (0 = play once).</summary>
    public required int RepeatCount { get; init; }
    public int Count => Bgra.Length;
}

/// <summary>Decodes animated GIFs with SkiaSharp. No UI types, so it can be tested without a window.</summary>
public static class GifDecoder
{
    /// <summary>Decoded frames are kept in memory; a GIF that would need more than this is shown as a still picture instead.</summary>
    public const long DefaultMaxBytes = 96L * 1024 * 1024;

    public static bool IsGif(ReadOnlySpan<byte> b) =>
        b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8' && (b[4] == '7' || b[4] == '9') && b[5] == 'a';

    /// <summary>Browsers treat delays of 10 ms or less (including 0) as 100 ms; otherwise a "0-delay" GIF would spin the CPU.</summary>
    public static int NormalizeDelay(int ms) => ms <= 10 ? 100 : ms;

    /// <summary>
    /// The frames of an animated GIF, or null when it isn't one (a single frame), is corrupt, or would use too much memory.
    /// Optimised GIFs store only the changed rectangle per frame; each frame is composed onto the one it builds on.
    /// </summary>
    public static GifFrames? Decode(byte[] bytes, long maxBytes = DefaultMaxBytes)
    {
        try
        {
            if (!IsGif(bytes)) return null;
            using var codec = SKCodec.Create(new SKMemoryStream(bytes));
            if (codec is null || codec.FrameCount < 2) return null;

            int w = codec.Info.Width, h = codec.Info.Height, n = codec.FrameCount;
            long frameBytes = (long)w * h * 4;
            if (w <= 0 || h <= 0 || frameBytes * n > maxBytes) return null;

            var infos = codec.FrameInfo;
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var frames = new byte[n][];
            var delays = new int[n];

            for (int i = 0; i < n; i++)
            {
                using var bitmap = new SKBitmap(info);
                int required = infos[i].RequiredFrame;
                if (required >= 0 && required < i) Copy(frames[required], bitmap);   // start from the frame this one builds on

                var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(i, required));
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) return null;

                frames[i] = new byte[frameBytes];
                System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), frames[i], 0, (int)frameBytes);
                delays[i] = NormalizeDelay(infos[i].Duration);
            }
            return new GifFrames { Width = w, Height = h, Bgra = frames, DelaysMs = delays, RepeatCount = codec.RepetitionCount };
        }
        catch { return null; } // corrupt file, missing native library...: fall back to a still image
    }

    static void Copy(byte[] from, SKBitmap to) => System.Runtime.InteropServices.Marshal.Copy(from, 0, to.GetPixels(), from.Length);
}

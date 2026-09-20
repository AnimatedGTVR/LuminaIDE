using LuminaIDE;
using Xunit;

namespace LuminaIDE.Tests;

public class GifTests
{
    static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    /// <summary>The colour of one pixel of one frame, as (R, G, B, A). Frames are premultiplied BGRA.</summary>
    static (byte R, byte G, byte B, byte A) Pixel(GifFrames g, int frame, int x, int y)
    {
        int i = (y * g.Width + x) * 4;
        var f = g.Bgra[frame];
        return (f[i + 2], f[i + 1], f[i], f[i + 3]);
    }

    [Theory]
    [InlineData("GIF89a", true)]
    [InlineData("GIF87a", true)]
    [InlineData("GIF88a", false)]
    [InlineData("\u0089PNG\r\n", false)]
    [InlineData("GIF", false)]
    [InlineData("", false)]
    public void Recognises_gif_data_by_its_header(string header, bool expected) =>
        Assert.Equal(expected, GifDecoder.IsGif(System.Text.Encoding.Latin1.GetBytes(header)));

    [Theory]
    [InlineData(0, 100)]     // browsers treat 0..10 ms as 100 ms, or a "no delay" GIF would spin
    [InlineData(10, 100)]
    [InlineData(11, 11)]
    [InlineData(50, 50)]
    [InlineData(2000, 2000)]
    public void Tiny_delays_are_slowed_down_like_browsers_do(int given, int expected) =>
        Assert.Equal(expected, GifDecoder.NormalizeDelay(given));

    [Fact]
    public void Decodes_every_frame_with_its_delay_and_loop_count()
    {
        var g = GifDecoder.Decode(Fixture("anim.gif"))!;
        Assert.NotNull(g);
        Assert.Equal((8, 8, 3), (g.Width, g.Height, g.Count));
        Assert.Equal([50, 200, 100], g.DelaysMs);            // the third frame's delay of 0 became 100
        Assert.Equal(-1, g.RepeatCount);                      // loops forever
        Assert.Equal((255, 0, 0, 255), Pixel(g, 0, 3, 3));    // red, green, blue
        Assert.Equal((0, 255, 0, 255), Pixel(g, 1, 3, 3));
        Assert.Equal((0, 0, 255, 255), Pixel(g, 2, 3, 3));
        Assert.All(g.Bgra, f => Assert.Equal(8 * 8 * 4, f.Length));
    }

    [Fact]
    public void Optimised_gifs_are_composed_from_the_frames_they_build_on()
    {
        // Frame 0 is all white; frame 1 only stores a red 2x2; frame 2 only stores a blue 2x2.
        var g = GifDecoder.Decode(Fixture("partial.gif"))!;
        Assert.Equal(3, g.Count);
        Assert.Equal(1, g.RepeatCount);                       // plays twice in total

        Assert.Equal((255, 255, 255, 255), Pixel(g, 0, 3, 3));
        Assert.Equal((255, 0, 0, 255), Pixel(g, 1, 3, 3));
        Assert.Equal((255, 255, 255, 255), Pixel(g, 1, 5, 5));   // frame 1 hasn't drawn the blue square yet
        // Frame 2 must still contain frame 1's red square AND the white background: this is the composition.
        Assert.Equal((255, 0, 0, 255), Pixel(g, 2, 3, 3));
        Assert.Equal((0, 0, 255, 255), Pixel(g, 2, 5, 5));
        Assert.Equal((255, 255, 255, 255), Pixel(g, 2, 0, 0));
    }

    [Fact]
    public void Things_that_are_not_animations_come_back_null_so_they_fall_back_to_a_still_image()
    {
        Assert.Null(GifDecoder.Decode(Fixture("single.gif")));                       // one frame: nothing to animate
        Assert.Null(GifDecoder.Decode("not a gif at all"u8.ToArray()));
        Assert.Null(GifDecoder.Decode([]));
        Assert.Null(GifDecoder.Decode(Fixture("anim.gif")[..40]));                   // truncated
        Assert.Null(GifDecoder.Decode(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 }));
    }

    [Fact]
    public void A_gif_that_would_need_too_much_memory_is_left_as_a_still_picture()
    {
        Assert.NotNull(GifDecoder.Decode(Fixture("slow.gif"), maxBytes: 4 * 8 * 8 * 4));   // exactly fits
        Assert.Null(GifDecoder.Decode(Fixture("slow.gif"), maxBytes: 4 * 8 * 8 * 4 - 1));  // one byte too many
    }
}

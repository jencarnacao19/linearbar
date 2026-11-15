using LinearBar.Core.Imaging;

namespace LinearBar.Core.Tests.Imaging;

public class AlphaBoundsTests
{
    /// <summary>Builds a transparent BGRA bitmap with one opaque rectangle.</summary>
    private static byte[] Bitmap(int width, int height, int x, int y, int w, int h, int stride = 0)
    {
        stride = stride == 0 ? width * 4 : stride;
        var pixels = new byte[stride * height];
        for (int row = y; row < y + h; row++)
        {
            for (int column = x; column < x + w; column++)
            {
                pixels[row * stride + column * 4 + 3] = 255;
            }
        }

        return pixels;
    }

    [Fact]
    public void Finds_the_box_around_visible_pixels()
    {
        byte[] pixels = Bitmap(256, 256, x: 10, y: 20, w: 48, h: 40);

        Assert.Equal((10, 20, 48, 40), AlphaBounds.Find(pixels, 256, 256, 256 * 4));
    }

    [Fact]
    public void Respects_stride_padding_and_the_alpha_threshold()
    {
        byte[] pixels = Bitmap(8, 8, x: 2, y: 3, w: 4, h: 2, stride: 40);
        pixels[0 * 40 + 0 * 4 + 3] = 5; // faint pixel below the threshold is ignored

        Assert.Equal((2, 3, 4, 2), AlphaBounds.Find(pixels, 8, 8, 40));
        Assert.Equal((0, 0, 6, 5), AlphaBounds.Find(pixels, 8, 8, 40, threshold: 1));
    }

    [Fact]
    public void Fully_transparent_bitmap_has_no_bounds_and_no_crop()
    {
        var bounds = AlphaBounds.Find(new byte[16 * 16 * 4], 16, 16, 64);

        Assert.Equal((0, 0, 0, 0), bounds);
        Assert.False(AlphaBounds.TryGetCrop(bounds, 16, 16, out _));
    }

    [Fact]
    public void Small_icon_in_the_corner_of_a_jumbo_bitmap_is_cropped()
    {
        var content = AlphaBounds.Find(Bitmap(256, 256, 0, 0, 48, 48), 256, 256, 1024);

        Assert.True(AlphaBounds.TryGetCrop(content, 256, 256, out var crop));
        Assert.Equal((0, 0, 48), crop);
    }

    [Fact]
    public void Centred_non_square_content_is_cropped_to_a_square_around_it()
    {
        // 40 wide, 20 tall, centred in a 256 bitmap.
        var content = (X: 108, Y: 118, Width: 40, Height: 20);

        Assert.True(AlphaBounds.TryGetCrop(content, 256, 256, out var crop));
        Assert.Equal((108, 108, 40), crop);
    }

    [Fact]
    public void Crop_square_is_kept_inside_the_bitmap()
    {
        // Wide content touching the top edge: the square cannot extend above row 0.
        var content = (X: 0, Y: 0, Width: 100, Height: 10);

        Assert.True(AlphaBounds.TryGetCrop(content, 256, 256, out var crop));
        Assert.Equal((0, 0, 100), crop);
    }

    [Fact]
    public void Icons_that_fill_most_of_the_bitmap_are_left_alone()
    {
        var content = AlphaBounds.Find(Bitmap(256, 256, 16, 16, 224, 224), 256, 256, 1024);

        Assert.False(AlphaBounds.TryGetCrop(content, 256, 256, out _));
    }
}

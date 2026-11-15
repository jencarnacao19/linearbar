using LinearBar.Core.Imaging;

namespace LinearBar.Core.Tests.Imaging;

public class IconPaletteTests
{
    private static byte[] Fill(int count, byte b, byte g, byte r, byte a)
    {
        var pixels = new byte[count * 4];
        for (int i = 0; i < count; i++)
        {
            pixels[i * 4] = b;
            pixels[i * 4 + 1] = g;
            pixels[i * 4 + 2] = r;
            pixels[i * 4 + 3] = a;
        }

        return pixels;
    }

    [Fact]
    public void Solid_colour_is_returned_as_is()
    {
        byte[] pixels = Fill(16, b: 255, g: 120, r: 30, a: 255);

        Assert.Equal(new Rgb(30, 120, 255), IconPalette.Dominant(pixels, 4, 4, 16));
    }

    [Fact]
    public void Saturated_pixels_outweigh_grey_ones()
    {
        // Half mid-grey outline, half vivid orange.
        byte[] grey = Fill(8, 128, 128, 128, 255);
        byte[] orange = Fill(8, 0, 140, 255, 255);
        byte[] pixels = [.. grey, .. orange];

        Rgb colour = IconPalette.Dominant(pixels, 4, 4, 16)!.Value;

        Assert.Equal(new Rgb(255, 140, 0), colour);
    }

    [Fact]
    public void Transparent_pixels_are_ignored_and_premultiplication_undone()
    {
        // Premultiplied green at 50 % alpha is stored as half intensity.
        byte[] half = Fill(4, 0, 100, 0, 128);
        byte[] clear = Fill(12, 0, 0, 255, 0);
        byte[] pixels = [.. half, .. clear];

        Rgb colour = IconPalette.Dominant(pixels, 4, 4, 16)!.Value;

        Assert.Equal(0, colour.R);
        Assert.InRange(colour.G, 195, 201);
    }

    [Fact]
    public void Colourless_or_empty_icons_have_no_tint()
    {
        Assert.Null(IconPalette.Dominant(Fill(16, 200, 200, 200, 255), 4, 4, 16));
        Assert.Null(IconPalette.Dominant(Fill(16, 0, 0, 255, 0), 4, 4, 16));
        Assert.Null(IconPalette.Dominant([], 0, 0, 0));
    }
}

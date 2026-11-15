namespace LinearBar.Core.Imaging;

/// <summary>An RGB colour with components from 0 to 255.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>Picks the colour that characterises an icon, used to tint its glass tile.</summary>
public static class IconPalette
{
    /// <summary>
    /// The icon's dominant vivid colour: an average weighted towards opaque, saturated
    /// pixels, so the tile picks up "the blue of the icon" rather than its outline or shadow.
    /// Returns null for icons that are (almost) colourless or empty.
    /// </summary>
    /// <param name="pixels">BGRA or premultiplied BGRA pixels, 4 bytes per pixel.</param>
    public static Rgb? Dominant(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        double r = 0, g = 0, b = 0, total = 0, coloured = 0, opaque = 0;

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 4;
                if (i + 3 >= pixels.Length)
                {
                    break;
                }

                byte alpha = pixels[i + 3];
                if (alpha < 128)
                {
                    continue;
                }

                // Undo premultiplication so semi-transparent edges keep their real colour.
                double scale = 255.0 / alpha;
                double pb = Math.Min(255, pixels[i] * scale);
                double pg = Math.Min(255, pixels[i + 1] * scale);
                double pr = Math.Min(255, pixels[i + 2] * scale);

                double max = Math.Max(pr, Math.Max(pg, pb));
                double min = Math.Min(pr, Math.Min(pg, pb));
                double saturation = max <= 0 ? 0 : (max - min) / max;

                opaque++;
                if (saturation < 0.25 || max < 40)
                {
                    continue;
                }

                coloured++;
                double weight = saturation * saturation * (alpha / 255.0);
                r += pr * weight;
                g += pg * weight;
                b += pb * weight;
                total += weight;
            }
        }

        // Grey or black-and-white icons: no tint.
        if (total <= 0 || opaque == 0 || coloured / opaque < 0.08)
        {
            return null;
        }

        return new Rgb(ToByte(r / total), ToByte(g / total), ToByte(b / total));
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}

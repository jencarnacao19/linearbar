namespace LinearBar.Core.Imaging;

/// <summary>
/// Finds the visible part of an icon bitmap.
/// </summary>
/// <remarks>
/// When an executable only ships small icons, the Windows shell still hands back a 256×256
/// "jumbo" bitmap — with the small icon in a corner (or centred) and the rest transparent.
/// Drawn as-is it would show up as a tiny icon in the dock, so such bitmaps are cropped to
/// their content first.
/// </remarks>
public static class AlphaBounds
{
    /// <summary>
    /// Bounding box of all pixels whose alpha is at least <paramref name="threshold"/>.
    /// </summary>
    /// <param name="pixels">Pixel data, 4 bytes per pixel with alpha as the 4th byte (BGRA/PBGRA).</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="stride">Bytes per row.</param>
    /// <param name="threshold">Minimum alpha for a pixel to count as visible.</param>
    /// <returns>The box, or an empty box (zero size) when nothing is visible.</returns>
    public static (int X, int Y, int Width, int Height) Find(ReadOnlySpan<byte> pixels, int width, int height, int stride, byte threshold = 16)
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;

        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                int alphaIndex = row + x * 4 + 3;
                if (alphaIndex >= pixels.Length)
                {
                    break;
                }

                if (pixels[alphaIndex] >= threshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        return maxX < 0 ? (0, 0, 0, 0) : (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// Decides whether a bitmap is mostly empty padding and, if so, which square to crop to.
    /// </summary>
    /// <param name="content">Result of <see cref="Find"/>.</param>
    /// <param name="width">Bitmap width in pixels.</param>
    /// <param name="height">Bitmap height in pixels.</param>
    /// <param name="crop">Square region containing the content, clamped to the bitmap.</param>
    /// <param name="minimumFill">
    /// Fraction of the bitmap the content must span (on its longer side) to be left alone.
    /// Real icons are drawn edge to edge, so anything well below that is padding.
    /// </param>
    /// <returns>True when the bitmap should be cropped to <paramref name="crop"/>.</returns>
    public static bool TryGetCrop((int X, int Y, int Width, int Height) content, int width, int height, out (int X, int Y, int Size) crop, double minimumFill = 0.6)
    {
        crop = default;

        if (content.Width <= 0 || content.Height <= 0 || width <= 0 || height <= 0)
        {
            return false;
        }

        int size = Math.Max(content.Width, content.Height);
        if (size >= Math.Min(width, height) * minimumFill)
        {
            return false;
        }

        // Centre the square on the content, then keep it inside the bitmap.
        int x = content.X + (content.Width - size) / 2;
        int y = content.Y + (content.Height - size) / 2;
        x = Math.Clamp(x, 0, Math.Max(0, width - size));
        y = Math.Clamp(y, 0, Math.Max(0, height - size));

        crop = (x, y, size);
        return true;
    }
}

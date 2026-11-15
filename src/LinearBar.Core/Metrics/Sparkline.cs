namespace LinearBar.Core.Metrics;

/// <summary>Converts a <see cref="SampleHistory"/> into drawable points.</summary>
public static class Sparkline
{
    /// <summary>
    /// Maps the samples of <paramref name="history"/> onto a <paramref name="width"/> ×
    /// <paramref name="height"/> box whose origin is top-left (y grows downwards).
    /// </summary>
    /// <remarks>
    /// The horizontal step is based on the history capacity, not the current count, so the
    /// newest sample is always pinned to the right edge and a partially filled history grows
    /// in from the right instead of stretching.
    /// </remarks>
    /// <param name="history">Samples to plot.</param>
    /// <param name="maximum">Value drawn at the top of the box. Larger samples are clamped.</param>
    /// <param name="width">Box width.</param>
    /// <param name="height">Box height.</param>
    /// <param name="points">Receives the points, oldest first. Must hold at least <c>history.Count</c> items.</param>
    /// <returns>Number of points written.</returns>
    public static int BuildPoints(SampleHistory history, double maximum, double width, double height, Span<(double X, double Y)> points)
    {
        int count = Math.Min(history.Count, points.Length);
        if (count == 0 || width <= 0 || height <= 0)
        {
            return 0;
        }

        if (!(maximum > 0))
        {
            maximum = 1;
        }

        double step = history.Capacity > 1 ? width / (history.Capacity - 1) : 0;
        int first = history.Count - count;

        for (int i = 0; i < count; i++)
        {
            double value = Math.Clamp(history[first + i], 0, maximum);
            double x = width - (count - 1 - i) * step;
            double y = height - value / maximum * height;
            points[i] = (x, y);
        }

        return count;
    }
}

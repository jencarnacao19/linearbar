namespace LinearBar.Core.Metrics;

/// <summary>Picks stable, round axis maxima for auto-scaled graphs (network throughput).</summary>
public static class NiceScale
{
    /// <summary>
    /// Smallest value of the form 1, 2 or 5 × 10ⁿ that is not below <paramref name="value"/>,
    /// but never less than <paramref name="minimum"/>. Using a few round steps keeps the graph
    /// from rescaling on every sample.
    /// </summary>
    public static double Ceiling(double value, double minimum)
    {
        if (!double.IsFinite(value) || value <= minimum)
        {
            return minimum > 0 ? minimum : 1;
        }

        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        double normalized = value / magnitude;

        double step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return step * magnitude;
    }
}

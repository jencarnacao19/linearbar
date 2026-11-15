using System.Globalization;

namespace LinearBar.Core.Metrics;

/// <summary>Human-readable byte sizes and transfer rates, using binary multiples like Windows does.</summary>
public static class ByteFormat
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Formats a size such as <c>7.8 GB</c>.</summary>
    public static string Size(double bytes, CultureInfo? culture = null) => Format(bytes, culture);

    /// <summary>Formats a rate such as <c>1.2 MB/s</c>.</summary>
    public static string Rate(double bytesPerSecond, CultureInfo? culture = null) => Format(bytesPerSecond, culture) + "/s";

    private static string Format(double value, CultureInfo? culture)
    {
        culture ??= CultureInfo.CurrentCulture;

        if (!double.IsFinite(value) || value < 0)
        {
            value = 0;
        }

        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Keep roughly three significant digits: 5 B, 12 KB, 1.2 MB, 85.3 MB, 512 MB.
        string format = unit == 0 || value >= 100 ? "0" : value >= 10 ? "0.#" : "0.##";
        string text = value.ToString(format, culture);

        // Rounding can turn 1023.6 into "1024": promote to the next unit instead.
        if (text == "1024" && unit < Units.Length - 1)
        {
            text = "1";
            unit++;
        }

        return text + " " + Units[unit];
    }
}

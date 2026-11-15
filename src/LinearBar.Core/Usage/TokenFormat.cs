using System.Globalization;

namespace LinearBar.Core.Usage;

/// <summary>Compact numbers and durations for small widgets.</summary>
public static class TokenFormat
{
    /// <summary>1234 → "1,2 mil", 1_250_000 → "1,25 M" (pt) — short enough for a chip.</summary>
    public static string Compact(double value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double abs = Math.Abs(value);
        return abs switch
        {
            >= 1e9 => (value / 1e9).ToString(abs >= 1e11 ? "0" : abs >= 1e10 ? "0.#" : "0.##", culture) + " B",
            >= 1e6 => (value / 1e6).ToString(abs >= 1e8 ? "0" : abs >= 1e7 ? "0.#" : "0.##", culture) + " M",
            >= 1e3 => (value / 1e3).ToString(abs >= 1e5 ? "0" : abs >= 1e4 ? "0.#" : "0.#", culture) + " k",
            _ => value.ToString("0", culture),
        };
    }

    /// <summary>Time until <paramref name="target"/>: "2 h 10 min", "35 min", "3 d 4 h".</summary>
    public static string Until(DateTimeOffset target, DateTimeOffset now)
    {
        TimeSpan left = target - now;
        if (left <= TimeSpan.Zero)
        {
            return "agora";
        }

        if (left.TotalDays >= 1)
        {
            return $"{(int)left.TotalDays} d {left.Hours} h";
        }

        if (left.TotalHours >= 1)
        {
            return $"{(int)left.TotalHours} h {left.Minutes:00} min";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min";
    }
}

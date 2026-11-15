using System.Globalization;
using System.Text.Json;

namespace LinearBar.Core.Weather;

/// <summary>Current conditions and a short forecast.</summary>
public sealed record WeatherReport
{
    public double Temperature { get; init; }

    public double ApparentTemperature { get; init; }

    public int Code { get; init; }

    public bool IsDay { get; init; } = true;

    public double WindSpeed { get; init; }

    public int Humidity { get; init; }

    /// <summary>Next hours: (time, temperature, precipitation probability %).</summary>
    public IReadOnlyList<(DateTime Time, double Temperature, int RainChance)> Hours { get; init; } = [];

    /// <summary>Next days: (date, min, max, code).</summary>
    public IReadOnlyList<(DateTime Date, double Min, double Max, int Code)> Days { get; init; } = [];
}

/// <summary>
/// Client-side half of the Open-Meteo weather API (https://open-meteo.com — free, no key):
/// builds the request URL and parses the response.
/// </summary>
public static class OpenMeteo
{
    public static string ForecastUrl(double latitude, double longitude, bool fahrenheit = false)
    {
        string lat = latitude.ToString("0.####", CultureInfo.InvariantCulture);
        string lon = longitude.ToString("0.####", CultureInfo.InvariantCulture);
        string unit = fahrenheit ? "&temperature_unit=fahrenheit" : string.Empty;
        return "https://api.open-meteo.com/v1/forecast"
            + $"?latitude={lat}&longitude={lon}"
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m"
            + "&hourly=temperature_2m,precipitation_probability"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min"
            + "&forecast_days=5&timezone=auto" + unit;
    }

    public static string GeocodingUrl(string city) =>
        "https://geocoding-api.open-meteo.com/v1/search?count=1&language=pt&name=" + Uri.EscapeDataString(city.Trim());

    /// <summary>Latitude, longitude and display name of the first geocoding result.</summary>
    public static (double Latitude, double Longitude, string Name)? ParseGeocoding(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement first = results[0];
        string name = first.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
        return (first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble(), name);
    }

    /// <summary>Parses a forecast response; <paramref name="now"/> picks the upcoming hours.</summary>
    public static WeatherReport ParseForecast(string json, DateTime now, int hours = 12)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        JsonElement current = root.GetProperty("current");

        var hourly = new List<(DateTime, double, int)>();
        if (root.TryGetProperty("hourly", out JsonElement h))
        {
            JsonElement times = h.GetProperty("time");
            JsonElement temps = h.GetProperty("temperature_2m");
            h.TryGetProperty("precipitation_probability", out JsonElement rain);
            for (int i = 0; i < times.GetArrayLength() && hourly.Count < hours; i++)
            {
                DateTime time = DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture);
                if (time < now.AddMinutes(-59))
                {
                    continue;
                }

                int chance = rain.ValueKind == JsonValueKind.Array && rain[i].ValueKind == JsonValueKind.Number ? rain[i].GetInt32() : 0;
                hourly.Add((time, Number(temps[i]), chance));
            }
        }

        var daily = new List<(DateTime, double, double, int)>();
        if (root.TryGetProperty("daily", out JsonElement d))
        {
            JsonElement dates = d.GetProperty("time");
            for (int i = 0; i < dates.GetArrayLength(); i++)
            {
                daily.Add((
                    DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture),
                    Number(d.GetProperty("temperature_2m_min")[i]),
                    Number(d.GetProperty("temperature_2m_max")[i]),
                    (int)Number(d.GetProperty("weather_code")[i])));
            }
        }

        return new WeatherReport
        {
            Temperature = Number(current.GetProperty("temperature_2m")),
            ApparentTemperature = current.TryGetProperty("apparent_temperature", out JsonElement a) ? Number(a) : double.NaN,
            Humidity = current.TryGetProperty("relative_humidity_2m", out JsonElement hu) ? (int)Number(hu) : 0,
            WindSpeed = current.TryGetProperty("wind_speed_10m", out JsonElement w) ? Number(w) : 0,
            Code = (int)Number(current.GetProperty("weather_code")),
            IsDay = !current.TryGetProperty("is_day", out JsonElement day) || Number(day) != 0,
            Hours = hourly,
            Days = daily,
        };
    }

    /// <summary>Description (pt-PT) and icon name for a WMO weather code.</summary>
    public static (string Description, string Icon) Describe(int code, bool isDay = true) => code switch
    {
        0 => ("Céu limpo", isDay ? "weather-sunny" : "weather-clear-night"),
        1 => ("Quase limpo", isDay ? "weather-sunny" : "weather-clear-night"),
        2 => ("Parcialmente nublado", isDay ? "weather-partly-cloudy" : "weather-cloudy-night"),
        3 => ("Nublado", "weather-cloudy"),
        45 or 48 => ("Nevoeiro", "weather-fog"),
        51 or 53 or 55 => ("Chuvisco", "weather-drizzle"),
        56 or 57 => ("Chuvisco gelado", "weather-drizzle"),
        61 => ("Chuva fraca", "weather-rain"),
        63 => ("Chuva", "weather-rain"),
        65 => ("Chuva forte", "weather-rain"),
        66 or 67 => ("Chuva gelada", "weather-rain"),
        71 or 73 or 75 or 77 => ("Neve", "weather-snow"),
        80 or 81 or 82 => ("Aguaceiros", "weather-rain"),
        85 or 86 => ("Aguaceiros de neve", "weather-snow"),
        95 => ("Trovoada", "weather-storm"),
        96 or 99 => ("Trovoada com granizo", "weather-storm"),
        _ => ("—", "weather-cloudy"),
    };

    private static double Number(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number ? element.GetDouble() : 0;
}

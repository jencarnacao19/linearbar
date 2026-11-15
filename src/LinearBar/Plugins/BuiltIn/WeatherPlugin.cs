using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using LinearBar.Core.Plugins;
using LinearBar.Core.Weather;

namespace LinearBar.Plugins.BuiltIn;

/// <summary>
/// Current weather and forecast from Open-Meteo (free, no account). Location: the
/// <c>latitude</c>/<c>longitude</c> options, else the <c>city</c> option, else an
/// approximate location from the public IP address.
/// </summary>
internal sealed class WeatherPlugin : PollingPlugin
{
    private static readonly HttpClient Http = CreateClient();

    private (double Latitude, double Longitude, string Name)? _location;
    private string? _locationKey;

    public override string Id => "weather";

    public override string Name => "Meteorologia";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(15);

    protected override async Task<PluginUpdate> ProduceAsync(CancellationToken cancellation)
    {
        (double latitude, double longitude, string place) = await ResolveLocationAsync(cancellation).ConfigureAwait(false);
        bool fahrenheit = string.Equals(Config.GetString("units"), "fahrenheit", StringComparison.OrdinalIgnoreCase);

        string json = await Http.GetStringAsync(OpenMeteo.ForecastUrl(latitude, longitude, fahrenheit), cancellation).ConfigureAwait(false);
        WeatherReport report = OpenMeteo.ParseForecast(json, DateTime.Now);
        return new PluginUpdate(Render(report, place));
    }

    internal static PluginOutput Render(WeatherReport report, string place)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        (string description, string icon) = OpenMeteo.Describe(report.Code, report.IsDay);

        var rows = new List<PluginRow>
        {
            new() { Icon = "thermometer", Title = "Sensação térmica", Value = $"{report.ApparentTemperature:0}°" },
            new() { Icon = "droplet", Title = "Humidade", Value = $"{report.Humidity} %" },
            new() { Icon = "wind", Title = "Vento", Value = $"{report.WindSpeed:0} km/h" },
        };

        int maxRain = report.Hours.Take(6).Select(h => h.RainChance).DefaultIfEmpty(0).Max();
        if (maxRain > 0)
        {
            rows.Add(new PluginRow { Icon = "weather-rain", Title = "Chuva nas próximas 6 h", Value = $"{maxRain} %", Progress = maxRain / 100.0, Color = "#5AA9FF" });
        }

        foreach ((DateTime date, double min, double max, int code) in report.Days.Take(5))
        {
            (string dayDescription, string dayIcon) = OpenMeteo.Describe(code);
            string day = date.Date == DateTime.Today ? "Hoje" : culture.TextInfo.ToTitleCase(date.ToString("dddd", culture));
            rows.Add(new PluginRow { Icon = dayIcon, Title = day, Detail = dayDescription, Value = $"{min:0}° / {max:0}°" });
        }

        return new PluginOutput
        {
            Text = $"{report.Temperature:0}°",
            Icon = icon,
            Color = report.IsDay ? "#FFC54D" : "#9DB4FF",
            Graph = report.Hours.Select(h => h.Temperature).ToList(),
            Tooltip = $"{place}\n{description}, {report.Temperature:0}° (sensação {report.ApparentTemperature:0}°)",
            Panel = new PluginPanel
            {
                Title = place,
                Subtitle = $"{description} · {report.Temperature:0}°",
                Rows = rows,
                Buttons = [new PluginAction { Title = "Atualizar", Icon = "refresh", Action = "refresh" }],
            },
        };
    }

    private async Task<(double, double, string)> ResolveLocationAsync(CancellationToken cancellation)
    {
        double? latitude = Config.GetNumber("latitude");
        double? longitude = Config.GetNumber("longitude");
        string? city = Config.GetString("city");
        string key = $"{latitude}|{longitude}|{city}";

        if (_location is { } cached && key == _locationKey)
        {
            return cached;
        }

        (double, double, string) location;
        if (latitude is { } lat && longitude is { } lon)
        {
            location = (lat, lon, string.IsNullOrWhiteSpace(city) ? $"{lat:0.##}, {lon:0.##}" : city!);
        }
        else if (!string.IsNullOrWhiteSpace(city))
        {
            string json = await Http.GetStringAsync(OpenMeteo.GeocodingUrl(city), cancellation).ConfigureAwait(false);
            location = OpenMeteo.ParseGeocoding(json) ?? throw new InvalidOperationException($"cidade '{city}' não encontrada");
        }
        else
        {
            // Approximate location from the IP address; set "city" in the settings to avoid this.
            string json = await Http.GetStringAsync("https://ipapi.co/json/", cancellation).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            location = (
                root.GetProperty("latitude").GetDouble(),
                root.GetProperty("longitude").GetDouble(),
                root.TryGetProperty("city", out JsonElement name) ? name.GetString() ?? "Aqui" : "Aqui");
        }

        _location = location;
        _locationKey = key;
        return location;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LinearBar/0.1");
        return client;
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LinearBar.Visuals;

/// <summary>
/// The icon set used by widgets and plugins: line icons on a 24 × 24 grid, drawn with round
/// caps (in the style of Lucide, ISC licence). Plugins refer to them by name.
/// </summary>
internal static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["weather-sunny"] = "M12 8a4 4 0 1 1 0 8a4 4 0 1 1 0-8Z M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41",
        ["weather-clear-night"] = "M12 3a6 6 0 0 0 9 9a9 9 0 1 1-9-9Z",
        ["weather-partly-cloudy"] = "M12 2v2 M4.93 4.93l1.41 1.41 M20 12h2 M19.07 4.93l-1.41 1.41 M15.95 12.65a4 4 0 0 0-5.93-4.13 M13 22H7a5 5 0 1 1 4.9-6H13a3 3 0 0 1 0 6Z",
        ["weather-cloudy-night"] = "M13 16a3 3 0 1 1 0 6H7a5 5 0 1 1 4.9-6Z M10.1 9A6 6 0 0 1 16 4a4.24 4.24 0 0 0 6 6a6 6 0 0 1-3 5.2",
        ["weather-cloudy"] = "M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z",
        ["weather-fog"] = "M4 14.9A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.24 M16 17H7 M17 21H9",
        ["weather-drizzle"] = "M4 14.9A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.24 M8 19v1 M8 14v1 M16 19v1 M16 14v1 M12 21v1 M12 16v1",
        ["weather-rain"] = "M4 14.9A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.24 M16 14v6 M8 14v6 M12 16v6",
        ["weather-snow"] = "M4 14.9A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.24 M8 15h.01 M8 19h.01 M12 17h.01 M12 21h.01 M16 15h.01 M16 19h.01",
        ["weather-storm"] = "M6 16.33A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 .5 8.97 M13 11l-4 6h6l-4 6",
        ["music"] = "M9 18V5l12-2v13 M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0Z M21 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0Z",
        ["play"] = "M6 3l14 9-14 9Z",
        ["pause"] = "M6 4h4v16H6Z M14 4h4v16h-4Z",
        ["next"] = "M5 4l10 8-10 8Z M19 5v14",
        ["previous"] = "M19 20L9 12l10-8Z M5 19V5",
        ["claude"] = "M12 2v6 M12 16v6 M2 12h6 M16 12h6 M4.9 4.9l4.2 4.2 M14.9 14.9l4.2 4.2 M4.9 19.1l4.2-4.2 M14.9 9.1l4.2-4.2",
        ["codex"] = "M16 18l6-6-6-6 M8 6l-6 6 6 6 M14 4l-4 16",
        ["agy"] = "M12 2a3 3 0 1 1 0 .01Z M12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6Z M2 12a10 4 0 1 0 20 0a10 4 0 1 0-20 0Z",
        ["sparkle"] = "M12 3l1.9 5.6 5.6 1.9-5.6 1.9L12 18l-1.9-5.6-5.6-1.9 5.6-1.9Z M19 3v4 M21 5h-4",
        ["gauge"] = "M12 14l4-4 M3.34 19a10 10 0 1 1 17.32 0",
        ["clock"] = "M12 2a10 10 0 1 1 0 20a10 10 0 1 1 0-20Z M12 6v6l4 2",
        ["calendar"] = "M8 2v4 M16 2v4 M3 10h18 M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z",
        ["refresh"] = "M3 12a9 9 0 0 1 9-9a9.75 9.75 0 0 1 6.74 2.74L21 8 M21 3v5h-5 M21 12a9 9 0 0 1-9 9a9.75 9.75 0 0 1-6.74-2.74L3 16 M8 16H3v5",
        ["warning"] = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0Z M12 9v4 M12 17h.01",
        ["thermometer"] = "M14 4v10.54a4 4 0 1 1-4 0V4a2 2 0 0 1 4 0Z",
        ["droplet"] = "M12 22a7 7 0 0 0 7-7c0-2-1-3.9-3-5.5s-3.5-4-4-6.5c-.5 2.5-2 4.9-4 6.5C6 11.1 5 13 5 15a7 7 0 0 0 7 7Z",
        ["wind"] = "M17.7 7.7a2.5 2.5 0 1 1 1.8 4.3H2 M9.6 4.6A2 2 0 1 1 11 8H2 M12.6 19.4A2 2 0 1 0 14 16H2",
        ["globe"] = "M12 2a10 10 0 1 1 0 20a10 10 0 1 1 0-20Z M2 12h20 M12 2a15.3 15.3 0 0 1 4 10a15.3 15.3 0 0 1-4 10a15.3 15.3 0 0 1-4-10a15.3 15.3 0 0 1 4-10Z",
        ["cpu"] = "M6 6h12v12H6Z M9 9h6v6H9Z M9 1v3 M15 1v3 M9 20v3 M15 20v3 M20 9h3 M20 14h3 M1 9h3 M1 14h3",
        ["plug"] = "M12 22v-5 M9 8V2 M15 8V2 M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z",
        ["terminal"] = "M4 17l6-6-6-6 M12 19h8",
        ["bell"] = "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9 M10.3 21a1.94 1.94 0 0 0 3.4 0",
        ["timer"] = "M10 2h4 M12 14l3-3 M12 6a8 8 0 1 1 0 16a8 8 0 1 1 0-16Z",
        ["ping"] = "M2 12h4l3-9 6 18 3-9h4",
        ["github"] = "M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.4 5.4 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4 M9 18c-4.51 2-5-2-7-2",
        ["spotify"] = "M12 2a10 10 0 1 1 0 20a10 10 0 1 1 0-20Z M7 9.5c3.5-1 7-.6 10 1.2 M7.6 12.8c2.8-.8 5.6-.4 8 1 M8.2 15.9c2.2-.6 4.3-.3 6.2.8",
        ["pull-request"] = "M18 15a3 3 0 1 1 0 6a3 3 0 1 1 0-6Z M6 3a3 3 0 1 1 0 6a3 3 0 1 1 0-6Z M13 6h3a2 2 0 0 1 2 2v7 M6 9v12",
        ["shuffle"] = "M2 18h1.4c1.3 0 2.5-.6 3.3-1.7l6.1-8.6c.7-1.1 2-1.7 3.3-1.7H22 M18 2l4 4-4 4 M2 6h1.9c1.5 0 2.9.9 3.6 2.2 M22 18h-5.9c-1.3 0-2.6-.7-3.3-1.8l-.5-.8 M18 14l4 4-4 4",
        ["repeat"] = "M17 2l4 4-4 4 M3 11v-1a4 4 0 0 1 4-4h14 M7 22l-4-4 4-4 M21 13v1a4 4 0 0 1-4 4H3",
        ["video"] = "M16 10l5-3v10l-5-3Z M4 6h10a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2Z",
        ["external"] = "M15 3h6v6 M10 14L21 3 M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
        ["check"] = "M20 6L9 17l-5-5",
        ["dot"] = "M12 10a2 2 0 1 1 0 4a2 2 0 1 1 0-4Z",
        ["map-pin"] = "M20 10c0 6-8 12-8 12s-8-6-8-12a8 8 0 0 1 16 0Z M12 7a3 3 0 1 1 0 6a3 3 0 1 1 0-6Z",
        ["settings"] = "M12 15a3 3 0 1 0 0-6a3 3 0 0 0 0 6Z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The geometry of a named icon, in a 24 × 24 box; null when the name is unknown.</summary>
    public static Geometry? Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !Paths.TryGetValue(name, out string? data))
        {
            return null;
        }

        if (!Cache.TryGetValue(name, out Geometry? geometry))
        {
            geometry = Geometry.Parse(data);
            geometry.Freeze();
            Cache[name] = geometry;
        }

        return geometry;
    }

    /// <summary>Loads an image icon from a file path, or null.</summary>
    public static ImageSource? LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4 || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 96;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Draws a named icon (or text, or an image file) into <paramref name="bounds"/>.</summary>
    public static void Draw(DrawingContext dc, string? icon, Rect bounds, Brush brush, double strokeThickness = 1.9)
    {
        if (Get(icon) is { } geometry)
        {
            double scale = Math.Min(bounds.Width, bounds.Height) / 24;
            var pen = new Pen(brush, strokeThickness / scale)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();

            dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, bounds.X + (bounds.Width - 24 * scale) / 2, bounds.Y + (bounds.Height - 24 * scale) / 2));
            dc.DrawGeometry(null, pen, geometry);
            dc.Pop();
            return;
        }

        if (LoadFile(icon) is { } image)
        {
            dc.PushClip(SquircleGeometry.Create(bounds, bounds.Width * 0.25, Glass.Smoothing));
            dc.DrawImage(image, bounds);
            dc.Pop();
            return;
        }

        if (!string.IsNullOrWhiteSpace(icon))
        {
            // An emoji or a character.
            var text = new FormattedText(
                icon,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"),
                bounds.Height * 0.9,
                brush,
                1.0);
            dc.DrawText(text, new Point(bounds.X + (bounds.Width - text.Width) / 2, bounds.Y + (bounds.Height - text.Height) / 2));
        }
    }
}

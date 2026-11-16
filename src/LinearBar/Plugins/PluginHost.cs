using System.Collections.ObjectModel;
using System.IO;
using LinearBar.Core.Config;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;
using LinearBar.Plugins.BuiltIn;

namespace LinearBar.Plugins;

/// <summary>
/// Finds, starts and stops plugins, and exposes their widgets per island.
/// </summary>
/// <remarks>
/// Built-in plugins are compiled in. Script plugins are folders with a <c>plugin.json</c>,
/// in the <c>plugins</c> folder next to LinearBar.exe (shipped ones) and in
/// <c>%APPDATA%\LinearBar\plugins</c> (the user's own, which win on an id clash).
/// </remarks>
internal sealed class PluginHost : IDisposable
{
    /// <summary>Order of the widgets when nothing else decides it.</summary>
    private static readonly string[] PreferredOrder = ["spotify", "nowplaying", "calendar", "weather", "github", "claude", "codex", "agy"];

    private readonly List<PluginWidgetViewModel> _widgets = [];

    public static string UserPluginsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LinearBar", "plugins");

    public static string BundledPluginsFolder => Path.Combine(AppContext.BaseDirectory, "plugins");

    public ObservableCollection<PluginWidgetViewModel> Left { get; } = [];

    public ObservableCollection<PluginWidgetViewModel> Right { get; } = [];

    /// <summary>Problems found while loading script plugins, for the log and diagnostics.</summary>
    public List<string> LoadErrors { get; } = [];

    public IReadOnlyList<PluginWidgetViewModel> Widgets => _widgets;

    /// <summary>(Re)creates every plugin from the settings.</summary>
    public void Apply(BarSettings settings)
    {
        StopAll();
        LoadErrors.Clear();

        var plugins = new Dictionary<string, IPlugin>(StringComparer.Ordinal);
        foreach (IPlugin plugin in CreateBuiltIns(settings))
        {
            plugins[plugin.Id] = plugin;
        }

        foreach (string folder in new[] { BundledPluginsFolder, UserPluginsFolder })
        {
            foreach ((PluginManifest? manifest, string directory, string? error) in PluginManifestReader.LoadAll(folder))
            {
                if (manifest is null)
                {
                    LoadErrors.Add($"{directory}: {error}");
                    Log.Warn($"Plugin in '{directory}' ignored: {error}");
                    continue;
                }

                if (plugins.Remove(manifest.Id, out IPlugin? replaced))
                {
                    replaced.Dispose();
                }

                plugins[manifest.Id] = new ScriptPlugin(manifest);
            }
        }

        IEnumerable<IPlugin> ordered = plugins.Values
            .OrderBy(p => Array.IndexOf(PreferredOrder, p.Id) is var i && i >= 0 ? i : PreferredOrder.Length)
            .ThenBy(p => p.Id, StringComparer.Ordinal);

        foreach (IPlugin plugin in ordered)
        {
            PluginConfig? configured = settings.Plugins.GetValueOrDefault(plugin.Id);
            PluginConfig config = configured ?? new PluginConfig { Enabled = plugin.EnabledByDefault };
            if (!config.Enabled)
            {
                plugin.Dispose();
                continue;
            }

            PluginPlacement placement = config.Placement?.ToLowerInvariant() switch
            {
                "left" => PluginPlacement.Left,
                "right" => PluginPlacement.Right,
                _ => plugin.DefaultPlacement,
            };

            var widget = new PluginWidgetViewModel(plugin, placement);
            _widgets.Add(widget);
            (placement == PluginPlacement.Left ? Left : Right).Add(widget);

            try
            {
                plugin.Start(config);
            }
            catch (Exception ex)
            {
                Log.Error($"Plugin {plugin.Id} could not start", ex);
            }
        }

        Log.Info($"Plugins: {string.Join(", ", _widgets.Select(w => w.Id))}.");
    }

    public void Dispose() => StopAll();

    private static IEnumerable<IPlugin> CreateBuiltIns(BarSettings settings)
    {
        bool spotifyWidget = settings.Plugins.GetValueOrDefault("spotify")?.Enabled ?? true;
        yield return MediaPlugin.Spotify();
        yield return MediaPlugin.NowPlaying(spotifyWidget);
        yield return new CalendarPlugin();
        yield return new GitHubPlugin();
        yield return new WeatherPlugin();
        yield return new ClaudeUsagePlugin();
        yield return new CodexUsagePlugin();
    }

    private void StopAll()
    {
        foreach (PluginWidgetViewModel widget in _widgets)
        {
            try
            {
                widget.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn($"Plugin {widget.Id} did not stop cleanly: {ex.Message}");
            }
        }

        _widgets.Clear();
        Left.Clear();
        Right.Clear();
    }
}

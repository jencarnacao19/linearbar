using System.Windows.Media;
using LinearBar.Core.Config;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;

namespace LinearBar.Plugins;

/// <summary>A new state for a widget, optionally with an image that only built-in plugins can supply.</summary>
internal sealed record PluginUpdate(PluginOutput Output, ImageSource? Image = null);

/// <summary>
/// A source of widget content. Script plugins (<see cref="ScriptPlugin"/>) and built-in
/// plugins (Claude, Codex, weather, now playing) implement the same contract, so the bar
/// renders them all the same way.
/// </summary>
internal interface IPlugin : IDisposable
{
    string Id { get; }

    string Name { get; }

    PluginPlacement DefaultPlacement { get; }

    /// <summary>Whether the plugin runs when the settings do not mention it.</summary>
    bool EnabledByDefault => true;

    /// <summary>Raised whenever the widget should change. May fire on any thread.</summary>
    event Action<PluginUpdate>? Updated;

    /// <summary>Starts producing updates with the given settings.</summary>
    void Start(PluginConfig config);

    /// <summary>Asks for fresh content now.</summary>
    void Refresh();

    /// <summary>Handles an action the plugin declared (e.g. "media.next"); false when unknown.</summary>
    bool TryHandle(string action);
}

/// <summary>
/// Base for plugins that compute their content periodically on a background thread.
/// Exceptions are caught and shown on the widget instead of taking anything down.
/// </summary>
internal abstract class PollingPlugin : IPlugin
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Timer? _timer;
    private int _running;

    public abstract string Id { get; }

    public abstract string Name { get; }

    public virtual PluginPlacement DefaultPlacement => PluginPlacement.Right;

    public event Action<PluginUpdate>? Updated;

    protected PluginConfig Config { get; private set; } = new();

    /// <summary>Time between refreshes.</summary>
    protected abstract TimeSpan Interval { get; }

    public void Start(PluginConfig config)
    {
        lock (_gate)
        {
            Config = config;
            _cancellation?.Cancel();
            _cancellation = new CancellationTokenSource();
            _timer?.Dispose();
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, Interval);
        }
    }

    public void Refresh() => ThreadPool.QueueUserWorkItem(_ => Tick());

    public virtual bool TryHandle(string action) => false;

    public virtual void Dispose()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Computes the widget content. Runs on a thread-pool thread, never concurrently.</summary>
    protected abstract Task<PluginUpdate> ProduceAsync(CancellationToken cancellation);

    protected void Publish(PluginUpdate update) => Updated?.Invoke(update);

    private async void Tick()
    {
        // Skip a tick when the previous one is still running (slow network, big logs).
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return;
        }

        CancellationToken token;
        lock (_gate)
        {
            token = _cancellation?.Token ?? CancellationToken.None;
        }

        try
        {
            PluginUpdate update = await ProduceAsync(token).ConfigureAwait(false);
            if (!token.IsCancellationRequested)
            {
                Publish(update);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warn($"Plugin {Id} failed: {ex.Message}");
            Publish(new PluginUpdate(PluginOutput.Failure($"{Name}: {ex.Message}")));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }
}

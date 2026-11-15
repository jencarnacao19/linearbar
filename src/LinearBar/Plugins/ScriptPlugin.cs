using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using LinearBar.Core.Config;
using LinearBar.Core.Plugins;
using LinearBar.Infrastructure;

namespace LinearBar.Plugins;

/// <summary>
/// A plugin implemented by any program: PowerShell, Python, Node, a batch file or an .exe.
/// The program prints JSON (see <see cref="PluginOutput"/>) or plain text; in interval mode
/// it is run periodically, in stream mode it keeps running and every line is an update.
/// </summary>
/// <remarks>
/// Plugins run in their own process, with no console window, in their own folder, with
/// their options in <c>LINEARBAR_OPT_*</c> environment variables. A plugin that hangs is
/// killed after its timeout; one that keeps failing is backed off, never retried in a loop.
/// </remarks>
internal sealed class ScriptPlugin(PluginManifest manifest) : IPlugin
{
    private const int MaxOutputChars = 256 * 1024;

    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Process? _streamProcess;
    private PluginConfig _config = new();
    private int _failures;

    public string Id => manifest.Id;

    public string Name => manifest.Name ?? manifest.Id;

    public PluginPlacement DefaultPlacement => manifest.Placement;

    public bool EnabledByDefault => manifest.EnabledByDefault;

    public PluginManifest Manifest => manifest;

    public event Action<PluginUpdate>? Updated;

    public void Start(PluginConfig config)
    {
        Stop();
        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            _config = config;
            _cancellation = cancellation;
        }

        _ = manifest.Mode == PluginMode.Stream
            ? Task.Run(() => StreamLoopAsync(cancellation.Token))
            : Task.Run(() => IntervalLoopAsync(cancellation.Token));
    }

    public void Refresh()
    {
        if (manifest.Mode == PluginMode.Interval)
        {
            CancellationToken token;
            lock (_gate)
            {
                token = _cancellation?.Token ?? CancellationToken.None;
            }

            _ = Task.Run(() => RunOnceAsync(manifest.Run, token));
        }
    }

    public bool TryHandle(string action)
    {
        // Script plugins receive their own actions as an argument: "<run> --action <name>".
        if (manifest.Mode != PluginMode.Interval)
        {
            return false;
        }

        _ = Task.Run(() => RunOnceAsync(manifest.Run + " --action " + Quote(action), CancellationToken.None));
        return true;
    }

    /// <summary>Runs a plugin-relative command line from a menu entry ("run").</summary>
    public void RunCommand(string commandLine)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using Process? process = Process.Start(CreateStartInfo(commandLine, redirect: false));
                if (process is not null)
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Plugin {Id}: '{commandLine}' failed: {ex.Message}");
            }

            Refresh();
        });
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
            _cancellation = null;
            TryKill(_streamProcess);
            _streamProcess = null;
        }
    }

    private async Task IntervalLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool ok = await RunOnceAsync(manifest.Run, token).ConfigureAwait(false);
            TimeSpan wait = TimeSpan.FromSeconds(manifest.IntervalSeconds);
            if (!ok)
            {
                // Back off on repeated failures: 1×, 2×, 4× … up to 10 minutes.
                wait = TimeSpan.FromSeconds(Math.Min(600, manifest.IntervalSeconds * Math.Pow(2, Math.Min(6, _failures))));
            }

            try
            {
                await Task.Delay(wait, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<bool> RunOnceAsync(string commandLine, CancellationToken token)
    {
        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(commandLine, redirect: true) };
            process.Start();

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(token);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(manifest.TimeoutSeconds));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                TryKill(process);
                return Fail($"não respondeu em {manifest.TimeoutSeconds} s");
            }

            string output = Truncate(await stdout.ConfigureAwait(false));
            string errors = await stderr.ConfigureAwait(false);

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
            {
                return Fail($"saiu com o código {process.ExitCode}: {FirstLine(errors)}");
            }

            PluginOutput parsed = PluginOutputParser.Parse(output);
            if (parsed.Error is not null)
            {
                return Fail(parsed.Error);
            }

            _failures = 0;
            Updated?.Invoke(new PluginUpdate(parsed));
            return true;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return Fail(ex.Message);
        }
    }

    private async Task StreamLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var process = new Process { StartInfo = CreateStartInfo(manifest.Run, redirect: true) };
                process.Start();
                lock (_gate)
                {
                    _streamProcess = process;
                }

                _ = process.StandardError.ReadToEndAsync(token);
                while (!token.IsCancellationRequested && await process.StandardOutput.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    PluginOutput parsed = PluginOutputParser.Parse(line);
                    Updated?.Invoke(new PluginUpdate(parsed));
                    if (parsed.Error is null)
                    {
                        _failures = 0;
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    Fail($"terminou (código {(process.HasExited ? process.ExitCode : -1)})");
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                Fail(ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(6, _failures)))), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private ProcessStartInfo CreateStartInfo(string commandLine, bool redirect)
    {
        List<string> parts = CommandLine.Split(commandLine);
        if (parts.Count == 0)
        {
            throw new InvalidOperationException("comando vazio");
        }

        string file = parts[0];
        string local = Path.Combine(manifest.Directory, file);
        if (File.Exists(local))
        {
            file = local;
        }

        var info = new ProcessStartInfo(file)
        {
            WorkingDirectory = manifest.Directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
            StandardOutputEncoding = redirect ? Encoding.UTF8 : null,
            StandardErrorEncoding = redirect ? Encoding.UTF8 : null,
        };

        foreach (string argument in parts.Skip(1))
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["LINEARBAR_PLUGIN_ID"] = manifest.Id;
        info.Environment["LINEARBAR_PLUGIN_DIR"] = manifest.Directory;
        info.Environment["LINEARBAR_LANGUAGE"] = System.Globalization.CultureInfo.CurrentUICulture.Name;

        // Options: manifest defaults, overridden by the user's settings.
        PluginConfig config;
        lock (_gate)
        {
            config = _config;
        }

        foreach ((string key, JsonElement value) in manifest.Options.Concat(config.Options))
        {
            info.Environment[CommandLine.OptionVariable(key)] = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }

        // PowerShell prints UTF-16 or the console code page by default; ask for UTF-8.
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        return info;
    }

    private bool Fail(string message)
    {
        _failures++;
        Log.Warn($"Plugin {Id}: {message}");
        Updated?.Invoke(new PluginUpdate(PluginOutput.Failure($"{Name}: {message}")));
        return false;
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    private static string Quote(string value) => value.Contains(' ') ? "\"" + value + "\"" : value;

    private static string Truncate(string text) => text.Length <= MaxOutputChars ? text : text[..MaxOutputChars];

    private static string FirstLine(string text)
    {
        string trimmed = text.Trim();
        int newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed[..newline].Trim();
    }
}

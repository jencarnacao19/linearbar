using LinearBar.Core.Config;

namespace LinearBar.Core.Tests.Config;

public class AppKeyTests
{
    [Fact]
    public void Paths_compare_case_and_slash_insensitively()
    {
        Assert.Equal(AppKey.ForPath(@"C:\Program Files\App\app.exe"), AppKey.ForPath("\"c:/program files/APP/App.exe\" "));
        Assert.NotEqual(AppKey.ForPath(@"C:\a\app.exe"), AppKey.ForPath(@"C:\b\app.exe"));
    }

    [Fact]
    public void Pinned_key_prefers_aumid_then_target_then_path()
    {
        Assert.Equal(AppKey.ForAumid("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
            AppKey.ForPinned(new PinnedApp { Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", Path = @"C:\x.exe" }));

        Assert.Equal(AppKey.ForPath(@"C:\apps\code.exe"),
            AppKey.ForPinned(new PinnedApp { Path = @"C:\links\Code.lnk", Target = @"C:\apps\code.exe" }));

        Assert.Equal(AppKey.ForPath(@"C:\apps\code.exe"), AppKey.ForPinned(new PinnedApp { Path = @"C:\apps\code.exe" }));
        Assert.Null(AppKey.ForPinned(new PinnedApp { Name = "nothing" }));
    }

    [Fact]
    public void Window_key_uses_the_aumid_only_for_store_apps()
    {
        const string host = @"C:\Windows\System32\ApplicationFrameHost.exe";

        Assert.Equal(AppKey.ForAumid("Some.App!App"), AppKey.ForWindow(host, "Some.App!App", isStoreApp: true));
        Assert.Equal(AppKey.ForPath(@"C:\chrome.exe"), AppKey.ForWindow(@"C:\chrome.exe", "Chrome.Profile1", isStoreApp: false));
        Assert.Equal(AppKey.ForPath(host), AppKey.ForWindow(host, "", isStoreApp: true));
        Assert.Null(AppKey.ForWindow("", null, isStoreApp: false));
        Assert.True(AppKey.IsAumid(AppKey.ForAumid("x")));
        Assert.False(AppKey.IsAumid(AppKey.ForPath("x")));
    }
}

public class BarSettingsTests
{
    [Fact]
    public void Defaults_are_already_normalized()
    {
        var settings = new BarSettings();
        string before = SettingsStore.Serialize(settings);

        settings.Normalize();

        Assert.Equal(before, SettingsStore.Serialize(settings));
    }

    [Fact]
    public void Normalize_clamps_out_of_range_values_and_restores_missing_sections()
    {
        var settings = new BarSettings
        {
            BarHeight = 5000,
            BarOpacity = double.NaN,
            Dock = null!,
            Metrics = new MetricsSettings { IntervalMs = 1, HistoryLength = 100_000 },
            Clock = new ClockSettings { TimeFormat = " ", DateFormat = "" },
            Pinned = null!,
        };

        settings.Normalize();

        Assert.Equal(110, settings.BarHeight);
        Assert.Equal(0.5, settings.BarOpacity);
        Assert.NotNull(settings.Dock);
        Assert.Equal(250, settings.Metrics.IntervalMs);
        Assert.Equal(600, settings.Metrics.HistoryLength);
        Assert.Equal("HH:mm", settings.Clock.TimeFormat);
        Assert.Equal("ddd, d MMM", settings.Clock.DateFormat);
        Assert.Empty(settings.Pinned);
    }

    [Fact]
    public void Normalize_drops_unusable_and_duplicate_pins()
    {
        var settings = new BarSettings
        {
            Pinned =
            [
                new PinnedApp { Path = @"C:\a.exe" },
                new PinnedApp { Name = "no target" },
                new PinnedApp { Path = @"c:/A.EXE" },
                null!,
                new PinnedApp { Aumid = "Store.App!App" },
            ],
        };

        settings.Normalize();

        Assert.Equal(2, settings.Pinned.Count);
        Assert.Equal(@"C:\a.exe", settings.Pinned[0].Path);
        Assert.Equal("Store.App!App", settings.Pinned[1].Aumid);
    }
}

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "linearbar-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void First_load_creates_the_file_with_defaults()
    {
        using var store = new SettingsStore(FilePath);

        BarSettings settings = store.Load();

        Assert.True(File.Exists(FilePath));
        Assert.Null(store.LastError);
        Assert.Equal(64, settings.BarHeight);
        Assert.Contains("\"barHeight\": 64", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Saved_settings_round_trip()
    {
        using (var store = new SettingsStore(FilePath))
        {
            store.Load();
            Assert.True(store.Update(settings =>
            {
                settings.Dock.MaxScale = 2.25;
                settings.Clock.TimeFormat = "HH:mm:ss";
                settings.Pinned.Add(new PinnedApp { Name = "Notepad", Path = @"C:\Windows\notepad.exe" });
            }));
        }

        using var reopened = new SettingsStore(FilePath);
        BarSettings loaded = reopened.Load();

        Assert.Equal(2.25, loaded.Dock.MaxScale);
        Assert.Equal("HH:mm:ss", loaded.Clock.TimeFormat);
        PinnedApp pin = Assert.Single(loaded.Pinned);
        Assert.Equal("Notepad", pin.Name);
        Assert.Null(pin.Aumid);
    }

    [Fact]
    public void Partial_files_with_comments_keep_defaults_for_everything_else()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """
            {
              // only override what I care about
              "dock": { "iconSize": 32, },
              "metrics": { "showNetwork": false }
            }
            """);
        using var store = new SettingsStore(FilePath);

        BarSettings settings = store.Load();

        Assert.Null(store.LastError);
        Assert.Equal(32, settings.Dock.IconSize);
        Assert.Equal(1.75, settings.Dock.MaxScale);
        Assert.False(settings.Metrics.ShowNetwork);
        Assert.True(settings.Metrics.ShowCpu);
        Assert.Equal(64, settings.BarHeight);
    }

    [Fact]
    public void Invalid_file_keeps_the_previous_settings_and_reports_the_error()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ \"barHeight\": 60 }");
        using var store = new SettingsStore(FilePath);
        Assert.Equal(60, store.Load().BarHeight);

        File.WriteAllText(FilePath, "{ this is not json");
        BarSettings settings = store.Load();

        Assert.Equal(60, settings.BarHeight);
        Assert.NotNull(store.LastError);
        Assert.Equal("{ this is not json", File.ReadAllText(FilePath)); // never overwrite the user's file
    }

    [Fact]
    public async Task External_edits_are_picked_up_but_own_saves_are_not()
    {
        using var store = new SettingsStore(FilePath);
        store.Load();
        int raised = 0;
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += (_, _) =>
        {
            Interlocked.Increment(ref raised);
            changed.TrySetResult();
        };
        store.StartWatching();

        // Our own save must not echo back as a change.
        store.Update(settings => settings.BarHeight = 44);
        await Task.Delay(800);
        Assert.Equal(0, Volatile.Read(ref raised));

        File.WriteAllText(FilePath, "{ \"barHeight\": 64 }");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(64, store.Current.BarHeight);
    }
}

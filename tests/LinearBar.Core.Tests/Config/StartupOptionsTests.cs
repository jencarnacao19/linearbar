using LinearBar.Core.Config;

namespace LinearBar.Core.Tests.Config;

public class StartupOptionsTests
{
    [Fact]
    public void No_arguments_means_a_normal_start()
    {
        StartupOptions options = StartupOptions.Parse([]);

        Assert.False(options.SmokeTest);
        Assert.False(options.RestoreTaskbar);
        Assert.False(options.KeepTaskbar);
        Assert.False(options.Verbose);
        Assert.Null(options.SettingsPath);
        Assert.Null(options.LogPath);
        Assert.Null(options.ScreenshotDirectory);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void Switches_and_values_are_parsed_case_insensitively()
    {
        StartupOptions options = StartupOptions.Parse(
            ["--Smoke-Test", "--settings", @"C:\tmp\s.json", "--LOG", "run.log", "--screenshot", "shots", "--keep-taskbar", "--verbose"]);

        Assert.True(options.SmokeTest);
        Assert.True(options.KeepTaskbar);
        Assert.True(options.Verbose);
        Assert.Equal(@"C:\tmp\s.json", options.SettingsPath);
        Assert.Equal("run.log", options.LogPath);
        Assert.Equal("shots", options.ScreenshotDirectory);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void Restore_taskbar_is_recognised()
    {
        Assert.True(StartupOptions.Parse(["--restore-taskbar"]).RestoreTaskbar);
    }

    [Fact]
    public void Unknown_arguments_and_options_missing_their_value_are_reported()
    {
        StartupOptions options = StartupOptions.Parse(["--frobnicate", "--settings", "--verbose", "stray", "--log"]);

        Assert.Equal(["--frobnicate", "--settings", "stray", "--log"], options.Unknown);
        Assert.True(options.Verbose);
        Assert.Null(options.SettingsPath);
        Assert.Null(options.LogPath);
    }
}

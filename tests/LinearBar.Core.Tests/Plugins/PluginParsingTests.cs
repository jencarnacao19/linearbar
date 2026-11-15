using LinearBar.Core.Plugins;

namespace LinearBar.Core.Tests.Plugins;

public class PluginOutputParserTests
{
    [Fact]
    public void Plain_text_becomes_text_and_tooltip()
    {
        PluginOutput output = PluginOutputParser.Parse("18°\nLisboa\nnublado\n");

        Assert.Equal("18°", output.Text);
        Assert.Equal("Lisboa\nnublado", output.Tooltip);
        Assert.False(output.Hidden);
    }

    [Fact]
    public void Empty_output_hides_the_widget()
    {
        Assert.True(PluginOutputParser.Parse("  \n").Hidden);
        Assert.True(PluginOutputParser.Parse(null).Hidden);
    }

    [Fact]
    public void Json_output_is_read_case_insensitively_with_comments_and_clamped()
    {
        PluginOutput output = PluginOutputParser.Parse("""
            {
              // a comment
              "TEXT": "42 %",
              "icon": "claude",
              "bars": [ { "label": "5h", "value": 1.7 }, { "label": "semana", "value": "0.25" }, null ],
              "graph": [1, 2, 3],
              "panel": { "title": "Uso", "rows": [ { "title": "Hoje", "progress": -3 } ] },
              "menu": [ { "title": "Abrir", "open": "https://example.com" } ],
            }
            """);

        Assert.Null(output.Error);
        Assert.Equal("42 %", output.Text);
        Assert.Equal("claude", output.Icon);
        Assert.Equal([1.0, 0.25], output.Bars.Select(b => b.Value));
        Assert.Equal([1.0, 2.0, 3.0], output.Graph);
        Assert.Equal(0, output.Panel!.Rows[0].Progress);
        Assert.Equal("https://example.com", Assert.Single(output.Menu).Open);
    }

    [Fact]
    public void Broken_json_is_reported_as_an_error()
    {
        PluginOutput output = PluginOutputParser.Parse("{ \"text\": ");

        Assert.NotNull(output.Error);
        Assert.Equal("warning", output.Icon);
    }

    [Fact]
    public void Output_round_trips()
    {
        var output = new PluginOutput { Text = "x", Bars = [new PluginBar { Label = "a", Value = 0.5 }] };

        PluginOutput parsed = PluginOutputParser.Parse(PluginOutputParser.Serialize(output));

        Assert.Equal("x", parsed.Text);
        Assert.Equal(0.5, parsed.Bars[0].Value);
    }
}

public class PluginManifestTests
{
    [Fact]
    public void Valid_manifest_is_read_with_defaults()
    {
        PluginManifest? manifest = PluginManifestReader.Parse("""
            { "id": "agy", "run": "powershell -File agy.ps1", "mode": "interval", "intervalSeconds": 0,
              "placement": "left", "options": { "city": "Lisboa" } }
            """, @"C:\plugins\agy", out string? error);

        Assert.Null(error);
        Assert.NotNull(manifest);
        Assert.Equal("agy", manifest.Name);
        Assert.Equal(1, manifest.IntervalSeconds);
        Assert.Equal(PluginPlacement.Left, manifest.Placement);
        Assert.Equal(PluginMode.Interval, manifest.Mode);
        Assert.Equal("Lisboa", manifest.Options["city"].GetString());
        Assert.Equal(@"C:\plugins\agy", manifest.Directory);
    }

    [Theory]
    [InlineData("""{ "id": "Bad Id", "run": "x" }""")]
    [InlineData("""{ "id": "ok", "run": " " }""")]
    [InlineData("""{ "id": "-x", "run": "x" }""")]
    [InlineData("""not json""")]
    public void Invalid_manifests_are_rejected_with_a_reason(string json)
    {
        Assert.Null(PluginManifestReader.Parse(json, "dir", out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Plugins_are_discovered_in_subfolders()
    {
        string root = Path.Combine(Path.GetTempPath(), "lb-plugins-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "a"));
            Directory.CreateDirectory(Path.Combine(root, "b"));
            Directory.CreateDirectory(Path.Combine(root, "empty"));
            File.WriteAllText(Path.Combine(root, "a", "plugin.json"), """{ "id": "a", "run": "a.cmd" }""");
            File.WriteAllText(Path.Combine(root, "b", "plugin.json"), """{ "id": "B", "run": "b.cmd" }""");

            var all = PluginManifestReader.LoadAll(root);

            Assert.Equal(2, all.Count);
            Assert.Equal("a", all[0].Manifest!.Id);
            Assert.Null(all[1].Manifest);
            Assert.NotNull(all[1].Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("powershell -File meteo.ps1", new[] { "powershell", "-File", "meteo.ps1" })]
    [InlineData("\"C:\\Program Files\\x\\x.exe\" --a \"b c\"", new[] { "C:\\Program Files\\x\\x.exe", "--a", "b c" })]
    [InlineData("  a   \"\"  b ", new[] { "a", "", "b" })]
    public void Command_lines_are_split_like_windows(string commandLine, string[] expected)
    {
        Assert.Equal(expected, CommandLine.Split(commandLine));
    }

    [Fact]
    public void Option_names_become_environment_variables()
    {
        Assert.Equal("LINEARBAR_OPT_CITY", CommandLine.OptionVariable("city"));
        Assert.Equal("LINEARBAR_OPT_TOKEN_LIMIT", CommandLine.OptionVariable("token-limit"));
    }
}

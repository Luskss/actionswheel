using System.IO;
using AcoesRapidas.Config;

namespace AcoesRapidas.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private const string ValidJson = """
        {
          // comentários são aceitos
          "actions": [
            { "label": "Painel", "icon": "E774", "type": "url", "target": "http://192.168.0.10:8080" },
            { "label": "Teste", "type": "toast", "target": "Olá" },
          ]
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AcoesRapidasTests", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_directory, "config.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void ParsesInternalNetworkUrlsCommentsAndTrailingCommas()
    {
        var config = ConfigStore.Parse(ValidJson);

        Assert.Equal(2, config.Actions.Count);
        Assert.Equal("http://192.168.0.10:8080", config.Actions[0].Target);
    }

    [Fact]
    public void DefaultConfigurationIsValidAndHasSixActions()
    {
        var store = new ConfigStore(ConfigPath);
        Assert.Null(store.Load());

        var reloaded = ConfigStore.Parse(File.ReadAllText(ConfigPath));

        Assert.Equal(6, reloaded.Actions.Count);
    }

    [Theory]
    [InlineData("""{ "actions": [ { "label": "A", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "actions": [ { "label": "", "type": "toast", "target": "x" }, { "label": "B", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "actions": [ { "label": "A", "type": "toast", "target": "" }, { "label": "B", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "actions": [ { "label": "A", "type": "outro", "target": "x" }, { "label": "B", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "actions": [ { "label": "A", "type": "url", "target": "file:///C:/Windows" }, { "label": "B", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "actions": [ { "label": "A", "icon": "xyz", "type": "toast", "target": "x" }, { "label": "B", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "actions": [ null, { "label": "B", "type": "toast", "target": "x" } ] }""")]
    [InlineData("""{ "outro": [] }""")]
    [InlineData("{ quebrado")]
    public void RejectsInvalidConfigurations(string json)
    {
        Assert.Throws<ConfigException>(() => ConfigStore.Parse(json));
    }

    private static string Toasts(int count) =>
        string.Join(",", Enumerable.Repeat("""{ "label": "A", "type": "toast", "target": "x" }""", count));

    [Fact]
    public void AcceptsTwelveActionsAndRejectsThirteen()
    {
        Assert.Equal(12, ConfigStore.Parse($$"""{ "actions": [{{Toasts(12)}}] }""").Actions.Count);
        Assert.Throws<ConfigException>(() => ConfigStore.Parse($$"""{ "actions": [{{Toasts(13)}}] }"""));
    }

    [Fact]
    public void ParsesListWithChildrenAndIconPath()
    {
        var config = ConfigStore.Parse($$"""
            { "actions": [
              { "label": "Steam", "type": "menu", "iconPath": "%PROGRAMFILES(X86)%\\Steam\\steam.exe",
                "children": [ { "label": "Jogo", "type": "url", "target": "steam://rungameid/365670" } ] },
              {{Toasts(1)}}
            ] }
            """);

        var list = config.Actions[0];
        Assert.True(list.IsMenu);
        Assert.Equal("steam://rungameid/365670", list.Children![0].Target);
        Assert.Contains("steam.exe", list.IconPath);
    }

    [Fact]
    public void RejectsListWithoutChildrenOrWithTooManyChildren()
    {
        const string Empty = """{ "label": "L", "type": "menu", "children": [] }""";
        const string Missing = """{ "label": "L", "type": "menu" }""";
        var tooMany = $$"""{ "label": "L", "type": "menu", "children": [{{Toasts(9)}}] }""";

        foreach (var list in new[] { Empty, Missing, tooMany })
        {
            Assert.Throws<ConfigException>(() => ConfigStore.Parse($$"""{ "actions": [{{list}}, {{Toasts(1)}}] }"""));
        }
    }

    [Fact]
    public void ChildrenAreValidatedToo()
    {
        const string Bad = """{ "label": "L", "type": "menu", "children": [ { "label": "X", "type": "url", "target": "ftp://x" } ] }""";

        var error = Assert.Throws<ConfigException>(() => ConfigStore.Parse($$"""{ "actions": [{{Bad}}, {{Toasts(1)}}] }"""));
        Assert.Contains("\"X\"", error.Message);
    }

    [Fact]
    public void ListsCanNestTwiceButNotThreeTimes()
    {
        const string Leaf = """{ "label": "F", "type": "toast", "target": "x" }""";
        var twoLevels = $$"""{ "label": "A", "type": "menu", "children": [ { "label": "B", "type": "menu", "children": [{{Leaf}}] } ] }""";
        var threeLevels = $$"""{ "label": "A", "type": "menu", "children": [ { "label": "B", "type": "menu", "children": [ { "label": "C", "type": "menu", "children": [{{Leaf}}] } ] } ] }""";

        Assert.NotNull(ConfigStore.Parse($$"""{ "actions": [{{twoLevels}}, {{Toasts(1)}}] }"""));
        Assert.Throws<ConfigException>(() => ConfigStore.Parse($$"""{ "actions": [{{threeLevels}}, {{Toasts(1)}}] }"""));
    }

    [Theory]
    [InlineData("playpause")]
    [InlineData("next")]
    [InlineData("previous")]
    [InlineData("stop")]
    public void AcceptsMediaKeys(string key)
    {
        var json = $$"""{ "actions": [ { "label": "M", "type": "media", "target": "{{key}}" }, {{Toasts(1)}} ] }""";

        Assert.Equal(ActionTypes.Media, ConfigStore.Parse(json).Actions[0].Type);
    }

    [Fact]
    public void RejectsUnknownMediaKey()
    {
        Assert.Throws<ConfigException>(() => ConfigStore.Parse($$"""{ "actions": [ { "label": "M", "type": "media", "target": "volumeup" }, {{Toasts(1)}} ] }"""));
    }

    [Theory]
    [InlineData("http://192.168.0.10:8080", true)]
    [InlineData("https://example.com", true)]
    [InlineData("steam://rungameid/365670", true)]
    [InlineData("steam://open/main", true)]
    [InlineData("discord://", true)]
    [InlineData("discord://-/channels/123/456", true)]
    [InlineData("discord://-/channels/@me", true)]
    [InlineData("spotify:", true)]
    [InlineData("spotify:playlist:abc", true)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("steam://run id", false)]
    [InlineData("steam:\"--x", false)]
    [InlineData("", false)]
    public void OnlyWebAndKnownAppAddressesAreAllowed(string value, bool expected)
    {
        Assert.Equal(expected, ConfigStore.IsAllowedUrl(value));
    }

    [Fact]
    public void ListRoundTripsThroughSave()
    {
        var store = new ConfigStore(ConfigPath);
        var config = new AppConfig
        {
            Actions =
            [
                new()
                {
                    Label = "Discord",
                    Type = ActionTypes.Menu,
                    IconPath = "%LOCALAPPDATA%\\Discord\\app.ico",
                    Children = [new() { Label = "Mensagens diretas", Type = ActionTypes.Url, Target = "discord://-/channels/@me" }],
                },
                new() { Label = "Aviso", Type = ActionTypes.Toast, Target = "x" },
            ],
        };

        Assert.Null(store.Save(config));

        var reloaded = new ConfigStore(ConfigPath);
        Assert.Null(reloaded.Load());
        Assert.Equal("Mensagens diretas", reloaded.Current.Actions[0].Children![0].Label);
        Assert.Equal("%LOCALAPPDATA%\\Discord\\app.ico", reloaded.Current.Actions[0].IconPath);
        Assert.Null(reloaded.Current.Actions[1].Children);
    }

    [Fact]
    public void SettingsActionDoesNotNeedATarget()
    {
        var config = ConfigStore.Parse("""
            { "actions": [ { "label": "Ajustes", "type": "settings" }, { "label": "B", "type": "toast", "target": "x" } ] }
            """);

        Assert.Equal(ActionTypes.Settings, config.Actions[0].Type);
    }

    [Fact]
    public void SavedConfigurationCanBeReadBackWithAccents()
    {
        var store = new ConfigStore(ConfigPath);
        var config = new AppConfig
        {
            Actions =
            [
                new() { Label = "Ação rápida", Icon = "E774", Type = ActionTypes.Url, Target = "http://192.168.0.10:8080" },
                new() { Label = "Ajustes", Icon = "E713", Type = ActionTypes.Settings },
            ],
        };

        Assert.Null(store.Save(config));

        var reloaded = new ConfigStore(ConfigPath);
        Assert.Null(reloaded.Load());
        Assert.Equal("Ação rápida", reloaded.Current.Actions[0].Label);
        Assert.Contains("Ação rápida", File.ReadAllText(ConfigPath));
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void InvalidConfigurationIsNotSaved()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, ValidJson);
        var store = new ConfigStore(ConfigPath);
        store.Load();

        var error = store.Save(new AppConfig
        {
            Actions =
            [
                new() { Label = "A", Icon = "E774", Type = ActionTypes.Url, Target = "ftp://servidor" },
                new() { Label = "B", Icon = "E774", Type = ActionTypes.Toast, Target = "x" },
            ],
        });

        Assert.NotNull(error);
        Assert.Equal(ValidJson, File.ReadAllText(ConfigPath));
        Assert.Equal("Painel", store.Current.Actions[0].Label);
    }

    [Fact]
    public void InvalidFileKeepsLastValidConfiguration()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, ValidJson);
        var store = new ConfigStore(ConfigPath);
        store.Load();

        File.WriteAllText(ConfigPath, "{ quebrado");
        var error = store.Load();

        Assert.NotNull(error);
        Assert.Equal("Painel", store.Current.Actions[0].Label);
    }
}

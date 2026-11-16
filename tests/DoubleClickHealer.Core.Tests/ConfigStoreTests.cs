using DoubleClickHealer.Core;
using DoubleClickHealer.Core.Config;
using Xunit;

namespace DoubleClickHealer.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public ConfigStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "dch-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "config.json");
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        using var store = new ConfigStore(_path);

        Assert.True(store.Current.Enabled);
        Assert.True(store.Current.RunAtStartup);
        Assert.True(store.Current.StartMinimized);
        Assert.Equal(2, store.Current.Targets.Count);
        Assert.All(store.Current.Targets, rule => Assert.Equal(InputKind.Mouse, rule.Kind));
        Assert.All(store.Current.Targets, rule => Assert.Equal(80, rule.HoldMs));
    }

    [Fact]
    public void LegacyDefaultConfig_MigratesToNewDefaults()
    {
        File.WriteAllText(_path, """
            {
              "version": 1,
              "runAtStartup": false,
              "targets": [
                { "kind": "Mouse", "code": 1, "holdMs": 40 },
                { "kind": "Mouse", "code": 2, "holdMs": 40 },
                { "kind": "Mouse", "code": 4, "holdMs": 40 }
              ]
            }
            """);

        using var store = new ConfigStore(_path);

        Assert.Equal(2, store.Current.Version);
        Assert.True(store.Current.RunAtStartup);
        Assert.Equal(2, store.Current.Targets.Count);
        Assert.All(store.Current.Targets, rule => Assert.Equal(80, rule.HoldMs));
    }

    [Fact]
    public void CustomConfig_OnlyBumpsVersion()
    {
        File.WriteAllText(_path, """
            {
              "version": 1,
              "runAtStartup": false,
              "targets": [
                { "kind": "Mouse", "code": 1, "holdMs": 35 },
                { "kind": "Keyboard", "code": 65, "holdMs": 0 }
              ]
            }
            """);

        using var store = new ConfigStore(_path);

        Assert.Equal(2, store.Current.Version);
        Assert.False(store.Current.RunAtStartup);
        Assert.Equal(2, store.Current.Targets.Count);
        Assert.Equal(35, store.Current.Targets[0].HoldMs);
        Assert.Equal(InputKind.Keyboard, store.Current.Targets[1].Kind);
    }

    [Fact]
    public void SaveAndReload_RoundTrips()
    {
        using (var store = new ConfigStore(_path))
        {
            store.Save(new AppConfig
            {
                Enabled = false,
                RunAtStartup = true,
                ElevatedMode = true,
                Targets =
                [
                    KeyRule.Mouse(MouseButtons.Left, 55),
                    KeyRule.Keyboard(0x41, 0),
                ],
            });
        }

        using var reopened = new ConfigStore(_path);

        Assert.False(reopened.Current.Enabled);
        Assert.True(reopened.Current.RunAtStartup);
        Assert.True(reopened.Current.ElevatedMode);
        Assert.Equal(2, reopened.Current.Targets.Count);
        Assert.Equal(55, reopened.Current.Targets[0].HoldMs);
        Assert.Equal(InputKind.Keyboard, reopened.Current.Targets[1].Kind);
        Assert.Equal(0x41, reopened.Current.Targets[1].Code);
        Assert.Null(reopened.LastError);
    }

    [Fact]
    public void EnsureFile_WritesReadableJson()
    {
        using var store = new ConfigStore(_path);
        store.EnsureFile();

        Assert.True(File.Exists(_path));

        string json = File.ReadAllText(_path);
        Assert.Contains("\"targets\"", json);
        Assert.Contains("\"Mouse\"", json);
        Assert.Contains("\"holdMs\"", json);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults()
    {
        File.WriteAllText(_path, "{ this is not json");

        using var store = new ConfigStore(_path);

        Assert.NotNull(store.LastError);
        Assert.True(store.Current.Enabled);
    }

    [Fact]
    public void MissingOptionalFields_UseDefaults()
    {
        File.WriteAllText(_path, """
            { "targets": [ { "kind": "Keyboard", "code": 65, "holdMs": 0 } ] }
            """);

        using var store = new ConfigStore(_path);

        var rule = Assert.Single(store.Current.Targets);
        Assert.True(rule.Enabled);
        Assert.Equal(InputKind.Keyboard, rule.Kind);
        Assert.Equal(65, rule.Code);
        Assert.Equal(0, rule.HoldMs);
        Assert.Equal(20, rule.ChatterGapMs);
    }

    [Fact]
    public void Watcher_ReloadsExternalEdit()
    {
        using var store = new ConfigStore(_path);
        store.EnsureFile();
        store.StartWatching();

        using var signal = new ManualResetEventSlim(false);
        AppConfig? received = null;

        store.Changed += config =>
        {
            received = config;
            signal.Set();
        };

        File.WriteAllText(_path, """
            {
              "enabled": false,
              "targets": [
                { "kind": "Mouse", "code": 2, "holdMs": 88 }
              ]
            }
            """);

        Assert.True(signal.Wait(TimeSpan.FromSeconds(3)), "配置变更未被监听到");
        Assert.NotNull(received);
        Assert.False(received!.Enabled);
        Assert.Single(received.Targets);
        Assert.Equal(88, received.Targets[0].HoldMs);
    }

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
}

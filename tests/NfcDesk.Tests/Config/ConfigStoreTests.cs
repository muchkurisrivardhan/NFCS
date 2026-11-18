using NfcDesk.Core.Config;

namespace NfcDesk.Tests.Config;

public class ConfigStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nfcdesk-cfg-" + Guid.NewGuid().ToString("N"));
    private AppPaths Paths => new(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public void AppPaths_Layout_AndEnsureCreated()
    {
        var p = Paths;
        p.EnsureCreated();
        Assert.True(Directory.Exists(p.ScriptsDir));
        Assert.True(Directory.Exists(p.LogsDir));
        Assert.Equal(Path.Combine(_root, "config.json"), p.ConfigFile);
        Assert.Equal(Path.Combine(_root, "secret.bin"), p.SecretFile);
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var s = new ConfigStore(Paths).Snapshot();
        Assert.Equal(8765, s.Port);
        Assert.True(s.Notifications);
        Assert.False(s.FirewallPrompted);
        Assert.Empty(s.Tags);
    }

    [Fact]
    public void Tags_PersistAcrossReload_AndJsonIsCamelCase()
    {
        var p = Paths;
        var a = new ConfigStore(p);
        a.UpsertTag(new TagMapping { Tag = "desk-focus-01", Label = "Focus", Script = "focus.ps1" });
        a.RecordTap("desk-focus-01", new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));
        a.SetLastTs("desk-focus-01", 1763441000);

        var json = File.ReadAllText(p.ConfigFile);
        Assert.Contains("\"tapCount\": 1", json);
        Assert.Contains("\"lastTs\"", json);
        Assert.False(File.Exists(p.ConfigFile + ".tmp"));

        var b = new ConfigStore(p);
        var t = b.FindTag("desk-focus-01")!;
        Assert.Equal("Focus", t.Label);
        Assert.Equal(1, t.TapCount);
        Assert.NotNull(t.LastTapped);
        Assert.Equal(1763441000, b.GetLastTs("desk-focus-01"));
    }

    [Fact]
    public void RemoveTag_KeepsLastTs()
    {
        var s = new ConfigStore(Paths);
        s.UpsertTag(new TagMapping { Tag = "t", Script = "x.ps1" });
        s.SetLastTs("t", 5);
        Assert.True(s.RemoveTag("t"));
        Assert.False(s.RemoveTag("t"));
        Assert.Null(s.FindTag("t"));
        Assert.Equal(5, s.GetLastTs("t"));
    }

    [Fact]
    public void Upsert_ReplacesExisting()
    {
        var s = new ConfigStore(Paths);
        s.UpsertTag(new TagMapping { Tag = "t", Label = "a" });
        s.UpsertTag(new TagMapping { Tag = "t", Label = "b" });
        Assert.Single(s.Snapshot().Tags);
        Assert.Equal("b", s.FindTag("t")!.Label);
    }

    [Fact]
    public void Snapshot_IsDeepCopy()
    {
        var s = new ConfigStore(Paths);
        s.UpsertTag(new TagMapping { Tag = "t", Label = "a" });
        var snap = s.Snapshot();
        snap.Tags[0].Label = "changed";
        snap.Tags.Clear();
        Assert.Equal("a", s.FindTag("t")!.Label);
    }

    [Fact]
    public void CorruptFile_IsRenamed_AndDefaultsUsed()
    {
        var p = Paths;
        p.EnsureCreated();
        File.WriteAllText(p.ConfigFile, "{ not json");
        var s = new ConfigStore(p);
        Assert.Equal(8765, s.Snapshot().Port);
        Assert.Single(Directory.GetFiles(_root, "config.json.bad-*"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    [InlineData(-3)]
    public void InvalidPort_FallsBack(int port)
    {
        var p = Paths;
        p.EnsureCreated();
        File.WriteAllText(p.ConfigFile, $"{{\"port\": {port}}}");
        Assert.Equal(8765, new ConfigStore(p).Snapshot().Port);
    }

    [Fact]
    public void ValidPort_Kept()
    {
        var p = Paths;
        p.EnsureCreated();
        File.WriteAllText(p.ConfigFile, "{\"port\": 9000}");
        Assert.Equal(9000, new ConfigStore(p).Snapshot().Port);
    }

    [Fact]
    public void Update_FiresChanged_AndPersists()
    {
        var p = Paths;
        var s = new ConfigStore(p);
        int n = 0;
        s.Changed += () => n++;
        s.Update(c => c.Notifications = false);
        Assert.Equal(1, n);
        Assert.False(new ConfigStore(p).Snapshot().Notifications);
    }

    [Fact]
    public void ConcurrentWrites_AreSafe()
    {
        var p = Paths;
        var s = new ConfigStore(p);
        Parallel.For(0, 100, i => s.SetLastTs("tag" + (i % 5), i));
        Assert.Equal(5, new ConfigStore(p).Snapshot().LastTs.Count);
    }
}

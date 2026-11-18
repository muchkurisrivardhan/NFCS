using System.Text.Json;

namespace NfcDesk.Core.Config;

public sealed class ConfigStore : IReplayStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private AgentConfig _config;

    public event Action? Changed;

    public ConfigStore(AppPaths paths)
    {
        _paths = paths;
        _config = Load();
    }

    private AgentConfig Load()
    {
        var file = _paths.ConfigFile;
        if (!File.Exists(file)) return new AgentConfig();
        try
        {
            using var fs = File.OpenRead(file);
            var cfg = JsonSerializer.Deserialize<AgentConfig>(fs, Json) ?? throw new JsonException("null config");
            cfg.Tags ??= new();
            cfg.Tags.RemoveAll(t => t is null || string.IsNullOrWhiteSpace(t.Tag));
            foreach (var t in cfg.Tags) { t.Label ??= ""; t.Script ??= ""; }
            cfg.LastTs = cfg.LastTs is null
                ? new()
                : new Dictionary<string, long>(cfg.LastTs);
            if (cfg.Port < 1 || cfg.Port > 65535) cfg.Port = AgentConfig.DefaultPort;
            return cfg;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try
            {
                var bad = file + ".bad-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                File.Move(file, bad, overwrite: true);
            }
            catch { /* best effort */ }
            return new AgentConfig();
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(_paths.Root);
        var tmp = _paths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_config, Json));
        File.Move(tmp, _paths.ConfigFile, overwrite: true);
    }

    private void Mutate(Action<AgentConfig> mutate)
    {
        lock (_lock)
        {
            mutate(_config);
            SaveLocked();
        }
        Changed?.Invoke();
    }

    public AgentConfig Snapshot()
    {
        lock (_lock) return _config.Clone();
    }

    public void Update(Action<AgentConfig> mutate) => Mutate(mutate);

    public TagMapping? FindTag(string tag)
    {
        lock (_lock)
            return _config.Tags.FirstOrDefault(t => string.Equals(t.Tag, tag, StringComparison.Ordinal))?.Clone();
    }

    public void UpsertTag(TagMapping mapping)
    {
        var copy = mapping.Clone();
        Mutate(c =>
        {
            var i = c.Tags.FindIndex(t => t.Tag == copy.Tag);
            if (i >= 0) c.Tags[i] = copy; else c.Tags.Add(copy);
        });
    }

    public bool RemoveTag(string tag)
    {
        bool removed = false;
        lock (_lock)
        {
            removed = _config.Tags.RemoveAll(t => t.Tag == tag) > 0;
            if (removed) SaveLocked();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public void RecordTap(string tag, DateTimeOffset when)
    {
        Mutate(c =>
        {
            var t = c.Tags.FirstOrDefault(x => x.Tag == tag);
            if (t is null) return;
            t.LastTapped = when;
            t.TapCount++;
        });
    }

    public long? GetLastTs(string tag)
    {
        lock (_lock) return _config.LastTs.TryGetValue(tag, out var v) ? v : null;
    }

    public void SetLastTs(string tag, long ts)
    {
        Mutate(c => c.LastTs[tag] = ts);
    }
}

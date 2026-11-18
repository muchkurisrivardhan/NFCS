using System.Net;

namespace NfcDesk.Core.Net;

public sealed class RateLimiter(int maxPerSecond = 10, TimeProvider? time = null)
{
    private const int MaxEntries = 10_000;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<IPAddress, (long WindowStartMs, int Count)> _entries = new();
    private readonly object _gate = new();

    public bool TryAcquire(IPAddress ip)
    {
        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        lock (_gate)
        {
            if (_entries.Count >= MaxEntries) Purge(nowMs);
            if (_entries.TryGetValue(ip, out var e) && nowMs - e.WindowStartMs < 1000 && nowMs >= e.WindowStartMs)
            {
                if (e.Count >= maxPerSecond) return false;
                _entries[ip] = (e.WindowStartMs, e.Count + 1);
                return true;
            }
            if (!_entries.ContainsKey(ip) && _entries.Count >= MaxEntries)
            {
                // Still full of live entries: refuse rather than grow unbounded.
                return false;
            }
            _entries[ip] = (nowMs, 1);
            return true;
        }
    }

    private void Purge(long nowMs)
    {
        var stale = _entries.Where(kv => nowMs - kv.Value.WindowStartMs >= 1000 || nowMs < kv.Value.WindowStartMs)
                            .Select(kv => kv.Key).ToList();
        foreach (var k in stale) _entries.Remove(k);
    }
}

using System.Text;

namespace NfcDesk.Core.Logging;

public sealed class RollingFileLog : IActivityLog, IDisposable
{
    private const int RingSize = 200;
    private const int MaxTagLength = 64;

    private readonly string _dir;
    private readonly TimeProvider _time;
    private readonly int _keepDays;
    private readonly long _maxBytes;
    private readonly object _lock = new();
    private readonly Queue<TapLogEntry> _ring = new();

    private DateOnly _day;
    private int _index;
    private StreamWriter? _writer;
    private bool _disposed;

    public event Action<TapLogEntry>? TapLogged;

    public string CurrentFile { get; private set; } = "";

    public RollingFileLog(string logsDir, TimeProvider? time = null, int keepDays = 14, long maxBytesPerFile = 5 * 1024 * 1024)
    {
        _dir = logsDir;
        _time = time ?? TimeProvider.System;
        _keepDays = keepDays;
        _maxBytes = maxBytesPerFile;
        Directory.CreateDirectory(_dir);
        lock (_lock)
        {
            _day = Today();
            Cleanup();
            OpenForDay();
        }
    }

    private DateTimeOffset Now() => _time.GetLocalNow();
    private DateOnly Today() => DateOnly.FromDateTime(Now().DateTime);

    private string PathFor(DateOnly day, int index) =>
        Path.Combine(_dir, "nfcdesk-" + day.ToString("yyyyMMdd") + (index == 0 ? "" : "." + index) + ".log");

    private void OpenForDay()
    {
        _writer?.Dispose();
        _writer = null;
        _index = 0;
        while (File.Exists(PathFor(_day, _index)) && new FileInfo(PathFor(_day, _index)).Length >= _maxBytes) _index++;
        OpenCurrent();
    }

    private void OpenCurrent()
    {
        _writer?.Dispose();
        CurrentFile = PathFor(_day, _index);
        var fs = new FileStream(CurrentFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
    }

    private void Cleanup()
    {
        try
        {
            var cutoff = _day.AddDays(-_keepDays);
            foreach (var f in Directory.EnumerateFiles(_dir, "nfcdesk-*.log"))
            {
                var name = Path.GetFileName(f);
                if (name.Length < 16) continue;
                if (!DateOnly.TryParseExact(name.AsSpan(8, 8), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d)) continue;
                if (d < cutoff) { try { File.Delete(f); } catch { } }
            }
        }
        catch (IOException) { }
    }

    private static string Sanitize(string? s, int max = int.MaxValue)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(Math.Min(s.Length, max));
        foreach (var c in s)
        {
            if (sb.Length >= max) break;
            sb.Append(c is '\r' or '\n' or '\t' || char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString();
    }

    private void WriteLine(DateTimeOffset when, string line)
    {
        if (_disposed) return;
        var today = DateOnly.FromDateTime(when.DateTime);
        if (today != _day)
        {
            _day = today;
            Cleanup();
            OpenForDay();
        }
        else if (_writer!.BaseStream.Length >= _maxBytes)
        {
            _index++;
            OpenCurrent();
        }
        try { _writer!.WriteLine(line); }
        catch (IOException) { }
    }

    private static string Stamp(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz");

    public void LogTap(TapLogEntry entry)
    {
        var fields = new[]
        {
            Stamp(entry.Time), "TAP",
            Sanitize(entry.Source?.ToString()),
            Sanitize(entry.Tag, MaxTagLength),
            entry.Accepted ? "ok" : "denied",
            entry.Reason.ToString(),
        };
        var line = string.Join('\t', fields);
        if (!string.IsNullOrEmpty(entry.Detail)) line += "\t" + Sanitize(entry.Detail);
        lock (_lock)
        {
            _ring.Enqueue(entry);
            while (_ring.Count > RingSize) _ring.Dequeue();
            WriteLine(Now(), line);
        }
        TapLogged?.Invoke(entry);
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message);

    private void Write(string level, string message)
    {
        var now = Now();
        var line = Stamp(now) + "\t" + level + "\t" + Sanitize(message);
        lock (_lock) WriteLine(now, line);
    }

    public IReadOnlyList<TapLogEntry> Recent()
    {
        lock (_lock) return _ring.ToList();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}

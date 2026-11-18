using System.Net;
using NfcDesk.Core;
using NfcDesk.Core.Logging;

namespace NfcDesk.Tests.Logging;

public class RollingFileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nfcdesk-log-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now = start;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 15, 3, 123, TimeSpan.Zero);

    private static string[] Lines(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void WritesTapLines()
    {
        var t = new FakeTime(T0);
        using var log = new RollingFileLog(_dir, t);
        var ip = IPAddress.Parse("192.168.1.20");
        log.LogTap(new TapLogEntry(T0, ip, "desk-focus-01", true, DenyReason.None));
        log.LogTap(new TapLogEntry(T0, ip, "desk-focus-01", false, DenyReason.BadSignature));
        log.Info("hello");
        log.Error("boom", new InvalidOperationException("bad"));
        var lines = Lines(log.CurrentFile);
        Assert.EndsWith("nfcdesk-20261118.log", log.CurrentFile);
        Assert.Equal("2026-11-18T10:15:03.123+00:00\tTAP\t192.168.1.20\tdesk-focus-01\tok\tNone", lines[0]);
        Assert.Equal("2026-11-18T10:15:03.123+00:00\tTAP\t192.168.1.20\tdesk-focus-01\tdenied\tBadSignature", lines[1]);
        Assert.Equal("2026-11-18T10:15:03.123+00:00\tINFO\thello", lines[2]);
        Assert.StartsWith("2026-11-18T10:15:03.123+00:00\tERROR\tboom", lines[3]);
        Assert.Contains("InvalidOperationException", lines[3]);
    }

    [Fact]
    public void Sanitizes_Injection_AndTruncatesTag()
    {
        using var log = new RollingFileLog(_dir, new FakeTime(T0));
        log.Info("a\r\nINFO\tfake");
        log.LogTap(new TapLogEntry(T0, null, "x\r\n" + new string('t', 200), false, DenyReason.BadFormat, "d\nINJECT"));
        var lines = Lines(log.CurrentFile);
        Assert.Equal(2, lines.Length);
        Assert.Equal(3, lines[0].Split('\t').Length);
        var f = lines[1].Split('\t');
        Assert.Equal(64, f[3].Length);
        Assert.DoesNotContain('\r', lines[1]);
    }

    [Fact]
    public void DailyRollover()
    {
        var t = new FakeTime(T0);
        using var log = new RollingFileLog(_dir, t);
        log.Info("day1");
        t.Now = T0.AddDays(1);
        log.Info("day2");
        Assert.EndsWith("nfcdesk-20261119.log", log.CurrentFile);
        Assert.Single(Lines(Path.Combine(_dir, "nfcdesk-20261118.log")));
        Assert.Single(Lines(log.CurrentFile));
    }

    [Fact]
    public void SizeRollover()
    {
        using var log = new RollingFileLog(_dir, new FakeTime(T0), maxBytesPerFile: 200);
        for (int i = 0; i < 20; i++) log.Info("message number " + i);
        Assert.True(File.Exists(Path.Combine(_dir, "nfcdesk-20261118.1.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "nfcdesk-20261118.2.log")));
        var total = Directory.GetFiles(_dir).Sum(f => Lines(f).Length);
        Assert.Equal(20, total);
    }

    [Fact]
    public void Retention_DeletesOldFiles_OnStartup_AndRollover()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "nfcdesk-20260101.log"), "x");
        File.WriteAllText(Path.Combine(_dir, "nfcdesk-20260101.2.log"), "x");
        File.WriteAllText(Path.Combine(_dir, "nfcdesk-20261110.log"), "x");
        File.WriteAllText(Path.Combine(_dir, "other.log"), "x");
        var t = new FakeTime(T0);
        using var log = new RollingFileLog(_dir, t, keepDays: 14);
        Assert.False(File.Exists(Path.Combine(_dir, "nfcdesk-20260101.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "nfcdesk-20260101.2.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "nfcdesk-20261110.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "other.log")));

        t.Now = T0.AddDays(10);
        log.Info("x");
        Assert.False(File.Exists(Path.Combine(_dir, "nfcdesk-20261110.log")));
    }

    [Fact]
    public void Ring_CapsAt200_AndRaisesEvent()
    {
        using var log = new RollingFileLog(_dir, new FakeTime(T0));
        int events = 0;
        log.TapLogged += _ => events++;
        for (int i = 0; i < 250; i++)
            log.LogTap(new TapLogEntry(T0, null, "t" + i, true, DenyReason.None));
        var r = log.Recent();
        Assert.Equal(200, r.Count);
        Assert.Equal("t50", r[0].Tag);
        Assert.Equal("t249", r[^1].Tag);
        Assert.Equal(250, events);
    }
}

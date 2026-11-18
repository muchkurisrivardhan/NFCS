using System.Net;
using NfcDesk.Core.Net;
using NfcDesk.Tests.Protocol;

namespace NfcDesk.Tests.Net;

public class RateLimiterTests
{
    private static FakeTime Clock() => new(DateTimeOffset.FromUnixTimeSeconds(1763441000));

    [Fact]
    public void TenAllowedEleventhDenied_ThenRecovers()
    {
        var t = Clock();
        var rl = new RateLimiter(10, t);
        var ip = IPAddress.Parse("192.168.1.2");
        for (int i = 0; i < 10; i++) Assert.True(rl.TryAcquire(ip));
        Assert.False(rl.TryAcquire(ip));
        t.Advance(TimeSpan.FromMilliseconds(1001));
        Assert.True(rl.TryAcquire(ip));
    }

    [Fact]
    public void IpsIndependent()
    {
        var rl = new RateLimiter(10, Clock());
        var a = IPAddress.Parse("192.168.1.2");
        var b = IPAddress.Parse("192.168.1.3");
        for (int i = 0; i < 10; i++) rl.TryAcquire(a);
        Assert.False(rl.TryAcquire(a));
        Assert.True(rl.TryAcquire(b));
    }

    [Fact]
    public void ManyIps_StaleEntriesPurged()
    {
        var t = Clock();
        var rl = new RateLimiter(10, t);
        for (int i = 0; i < 10_000; i++) rl.TryAcquire(new IPAddress(new byte[] { 10, 0, (byte)(i >> 8), (byte)i }));
        t.Advance(TimeSpan.FromSeconds(2));
        Assert.True(rl.TryAcquire(IPAddress.Parse("10.9.9.9")));
    }
}

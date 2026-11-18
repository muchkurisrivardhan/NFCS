using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NfcDesk.Core;
using NfcDesk.Core.Net;
using NfcDesk.Core.Server;
using NfcDesk.Tests.Protocol;

namespace NfcDesk.Tests.Server;

public sealed class MemoryLog : IActivityLog
{
    public readonly List<TapLogEntry> Taps = new();
    public readonly List<string> Errors = new();
    public void LogTap(TapLogEntry e) { lock (Taps) Taps.Add(e); }
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { lock (Errors) Errors.Add(message); }
    public TapLogEntry[] Snapshot() { lock (Taps) return Taps.ToArray(); }
}

public sealed class TapServerTests : IAsyncLifetime
{
    private readonly Rig _rig = new();
    private readonly MemoryLog _log = new();
    private TapServer _server = null!;
    private readonly HttpClient _http = new();
    private Uri _tap = null!;
    private Uri _base = null!;

    public async Task InitializeAsync()
    {
        _server = new TapServer(_rig.Verifier, new RateLimiter(10, _rig.Time), _log, _rig.Time);
        await _server.StartAsync(0, new[] { IPAddress.Loopback });
        _base = _server.BoundUrls.Single();
        _tap = new Uri(_base, "/tap");
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    private static ByteArrayContent Raw(byte[] b) => new(b);

    private async Task<(HttpResponseMessage, string)> Post(byte[] body, string path = "/tap")
    {
        var r = await _http.PostAsync(new Uri(_base, path), Raw(body));
        return (r, await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ValidTap_Returns200_AndRaisesEvent()
    {
        var tcs = new TaskCompletionSource<TapVerdict>();
        _server.TapAccepted += v => tcs.TrySetResult(v);
        var (r, body) = await Post(Rig.Body("desk-focus-01", Rig.Now));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/json", r.Content.Headers.ContentType!.MediaType);
        Assert.Equal("{\"status\":\"ok\"}", body);
        Assert.False(r.Headers.Contains("Server"));
        var v = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("desk-focus-01", v.Tag);
        var e = Assert.Single(_log.Snapshot());
        Assert.True(e.Accepted);
        Assert.Equal(DenyReason.None, e.Reason);
        Assert.Equal("desk-focus-01", e.Tag);
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotAffectResponse()
    {
        _server.TapAccepted += _ => throw new InvalidOperationException("boom");
        var (r, _) = await Post(Rig.Body("desk-focus-01", Rig.Now));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        for (int i = 0; i < 50 && _log.Errors.Count == 0; i++) await Task.Delay(50);
        Assert.NotEmpty(_log.Errors);
    }

    [Fact]
    public async Task TsAsJsonString_WorksEndToEnd()
    {
        string sig = Rig.Sig("desk-focus-01", Rig.Now.ToString());
        var json = $"{{\"tag\":\"desk-focus-01\",\"ts\":\"{Rig.Now}\",\"sig\":\"{sig}\"}}";
        var (r, body) = await Post(Encoding.UTF8.GetBytes(json));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", body);
    }

    [Fact]
    public async Task BadSignature_Returns403Denied()
    {
        var (r, body) = await Post(Rig.Body("desk-focus-01", Rig.Now, new string('0', 64)));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("{\"status\":\"denied\"}", body);
        Assert.Equal(DenyReason.BadSignature, Assert.Single(_log.Snapshot()).Reason);
    }

    [Fact]
    public async Task GetTap_Returns403_LoggedBadFormat()
    {
        var r = await _http.GetAsync(_tap);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("{\"status\":\"denied\"}", await r.Content.ReadAsStringAsync());
        var e = Assert.Single(_log.Snapshot());
        Assert.Equal(DenyReason.BadFormat, e.Reason);
        Assert.Null(e.Tag);
    }

    [Fact]
    public async Task OtherPath_Returns404_EmptyBody_NotLogged()
    {
        var (r, body) = await Post(Rig.Body("desk-focus-01", Rig.Now), "/other");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("", body);
        Assert.Empty(_log.Snapshot());
    }

    [Fact]
    public async Task OversizedBody_Returns403()
    {
        var (r, body) = await Post(new byte[2000]);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("{\"status\":\"denied\"}", body);
        Assert.Equal(DenyReason.BadFormat, Assert.Single(_log.Snapshot()).Reason);
    }

    [Fact]
    public async Task RapidRequests_AreRateLimited()
    {
        var codes = new List<HttpStatusCode>();
        for (int i = 0; i < 11; i++)
            codes.Add((await Post(Encoding.UTF8.GetBytes("x"))).Item1.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, codes[10]);
        Assert.Equal(DenyReason.RateLimited, _log.Snapshot()[10].Reason);
        Assert.Equal(DenyReason.BadFormat, _log.Snapshot()[9].Reason);
    }

    [Fact]
    public async Task FailureResponses_AreByteIdentical_AndLogged()
    {
        _rig.State.Registered.Clear();
        var cases = new (byte[] Body, DenyReason Reason)[]
        {
            (Encoding.UTF8.GetBytes("garbage"), DenyReason.BadFormat),
            (Rig.Body("desk-focus-01", Rig.Now - 500), DenyReason.ClockSkew),
            (Rig.Body("desk-focus-01", Rig.Now, new string('a', 64)), DenyReason.BadSignature),
            (Rig.Body("desk-focus-01", Rig.Now), DenyReason.UnknownTag),
        };
        HttpResponseMessage? first = null; byte[]? firstBytes = null;
        foreach (var (body, reason) in cases)
        {
            var r = await _http.PostAsync(_tap, Raw(body));
            var bytes = await r.Content.ReadAsByteArrayAsync();
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
            if (first is null) { first = r; firstBytes = bytes; continue; }
            Assert.Equal(firstBytes, bytes);
            Assert.Equal(first.Content.Headers.ContentType, r.Content.Headers.ContentType);
            Assert.Equal(first.Headers.Select(h => h.Key).OrderBy(x => x), r.Headers.Select(h => h.Key).OrderBy(x => x));
        }
        Assert.Equal(cases.Select(c => c.Reason), _log.Snapshot().Select(e => e.Reason));
    }

    [Fact]
    public async Task Restart_Works()
    {
        await _server.StopAsync();
        await _server.StartAsync(0, new[] { IPAddress.Loopback });
        _base = _server.BoundUrls.Single();
        var (r, _) = await Post(Rig.Body("desk-focus-01", Rig.Now));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Start_WithNoBindableAddress_Throws()
    {
        await using var s = new TapServer(_rig.Verifier, new RateLimiter(10, _rig.Time), _log, _rig.Time);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            s.StartAsync(0, new[] { IPAddress.Parse("203.0.113.77") }));
        Assert.NotEmpty(_log.Errors);
    }
}

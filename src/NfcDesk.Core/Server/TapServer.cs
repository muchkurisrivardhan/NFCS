using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NfcDesk.Core.Net;
using NfcDesk.Core.Protocol;

namespace NfcDesk.Core.Server;

public sealed class TapServer : IAsyncDisposable
{
    private const int MaxRead = TapParser.MaxBodyBytes + 1;
    private static readonly byte[] OkBody = "{\"status\":\"ok\"}"u8.ToArray();
    private static readonly byte[] DeniedBody = "{\"status\":\"denied\"}"u8.ToArray();

    private readonly TapVerifier _verifier;
    private readonly RateLimiter _limiter;
    private readonly IActivityLog _log;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly List<WebApplication> _apps = new();
    private List<Uri> _urls = new();

    public TapServer(TapVerifier verifier, RateLimiter limiter, IActivityLog log, TimeProvider? time = null)
    {
        _verifier = verifier;
        _limiter = limiter;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    public event Action<TapVerdict>? TapAccepted;

    public IReadOnlyList<Uri> BoundUrls => _urls;

    public async Task StartAsync(int port, IEnumerable<IPAddress> bindAddresses, CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_apps.Count > 0) await StopCoreAsync().ConfigureAwait(false);

            var urls = new List<Uri>();
            // One host per address so a failure to bind one interface cannot affect the others.
            foreach (var addr in bindAddresses.Distinct())
            {
                WebApplication? app = null;
                try
                {
                    app = Build(addr, port);
                    await app.StartAsync(ct).ConfigureAwait(false);
                    var feature = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                                     .Features.Get<IServerAddressesFeature>();
                    foreach (var a in feature?.Addresses ?? Array.Empty<string>())
                        urls.Add(new Uri(a));
                    _apps.Add(app);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.Error($"Could not listen on {addr}:{port}", ex);
                    if (app is not null) { try { await app.DisposeAsync().ConfigureAwait(false); } catch { } }
                }
            }

            if (_apps.Count == 0)
                throw new InvalidOperationException("Could not bind the tap server to any address.");
            _urls = urls;
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        var apps = _apps.ToList();
        _apps.Clear();
        _urls = new List<Uri>();
        foreach (var app in apps)
        {
            try { await app.StopAsync().ConfigureAwait(false); }
            catch (Exception ex) { _log.Error("Error stopping tap server", ex); }
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
    }

    private WebApplication Build(IPAddress addr, int port)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = null });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Listen(addr, port);
        });
        var app = builder.Build();
        app.Run(HandleAsync);
        return app;
    }

    private async Task HandleAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path;
        if (!path.Equals("/tap", StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var remote = ctx.Connection.RemoteIpAddress;
        TapVerdict verdict;
        try
        {
            if (!HttpMethods.IsPost(ctx.Request.Method))
            {
                verdict = TapVerdict.Deny(DenyReason.BadFormat);
            }
            else if (remote is null)
            {
                verdict = TapVerdict.Deny(DenyReason.NonLanSource);
            }
            else if (!_limiter.TryAcquire(remote))
            {
                verdict = TapVerdict.Deny(DenyReason.RateLimited);
            }
            else
            {
                var body = await ReadBodyAsync(ctx.Request.Body, ctx.RequestAborted).ConfigureAwait(false);
                verdict = _verifier.Verify(remote, body);
            }
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _log.Error("Unexpected error handling tap", ex);
            verdict = TapVerdict.Deny(DenyReason.BadFormat);
        }

        try
        {
            _log.LogTap(new TapLogEntry(_time.GetUtcNow(), remote, verdict.Tag, verdict.Accepted, verdict.Reason));
        }
        catch (Exception ex) { _log.Error("Failed to log tap", ex); }

        if (verdict.Accepted)
        {
            Raise(verdict);
            await WriteAsync(ctx, 200, OkBody).ConfigureAwait(false);
        }
        else
        {
            await WriteAsync(ctx, 403, DeniedBody).ConfigureAwait(false);
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, byte[] body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        ctx.Response.ContentLength = body.Length;
        await ctx.Response.Body.WriteAsync(body).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBodyAsync(Stream s, CancellationToken ct)
    {
        var buf = new byte[MaxRead];
        int total = 0;
        while (total < MaxRead)
        {
            int n = await s.ReadAsync(buf.AsMemory(total, MaxRead - total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return buf.AsSpan(0, total).ToArray();
    }

    private void Raise(TapVerdict verdict)
    {
        var handler = TapAccepted;
        if (handler is null) return;
        _ = Task.Run(() =>
        {
            foreach (var d in handler.GetInvocationList())
            {
                try { ((Action<TapVerdict>)d)(verdict); }
                catch (Exception ex) { _log.Error("TapAccepted handler failed", ex); }
            }
        });
    }
}

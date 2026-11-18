using System.Net;
using System.Text;
using NfcDesk.Core;
using NfcDesk.Core.Protocol;

namespace NfcDesk.Tests.Protocol;

public sealed class FakeTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan t) => Now += t;
}

public sealed class FakeSecret(string? secret) : ISecretProvider
{
    public string? Secret = secret;
    public string? GetSecret() => Secret;
}

public sealed class FakeReplay : IReplayStore
{
    public readonly Dictionary<string, long> Map = new();
    public long? GetLastTs(string tag) => Map.TryGetValue(tag, out var v) ? v : null;
    public void SetLastTs(string tag, long ts) => Map[tag] = ts;
}

public sealed class FakeState : IAgentState
{
    public HashSet<string> Registered = new() { "desk-focus-01" };
    public bool LearningMode { get; set; }
    public bool Paused { get; set; }
    public bool IsTagRegistered(string tag) => Registered.Contains(tag);
}

public sealed class Rig
{
    public const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const long Now = 1763441000;
    public static readonly IPAddress Lan = IPAddress.Parse("192.168.1.50");

    public FakeTime Time = new(DateTimeOffset.FromUnixTimeSeconds(Now));
    public FakeSecret Secrets = new(Secret);
    public FakeReplay Replay = new();
    public FakeState State = new();
    public TapVerifier Verifier;

    public Rig() { Verifier = new TapVerifier(Secrets, Replay, State, Time); }

    public static string Sig(string tag, string ts, string secret = Secret) => TapSignature.Compute(tag, ts, secret);

    public static byte[] Body(string tag, long ts, string? sig = null, string? secret = null)
        => Encoding.UTF8.GetBytes($"{{\"tag\":\"{tag}\",\"ts\":{ts},\"sig\":\"{sig ?? Sig(tag, ts.ToString(), secret ?? Secret)}\"}}");

    public TapVerdict Send(byte[] body, IPAddress? ip = null) => Verifier.Verify(ip ?? Lan, body);
    public TapVerdict Send(string json, IPAddress? ip = null) => Send(Encoding.UTF8.GetBytes(json), ip);
}

using System.Net;
using NfcDesk.Core.Net;

namespace NfcDesk.Core.Protocol;

public sealed class TapVerifier(ISecretProvider secrets, IReplayStore replay, IAgentState state, TimeProvider? time = null)
{
    private const long MaxSkewSeconds = 30;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _gate = new();

    public TapVerdict Verify(IPAddress? source, ReadOnlySpan<byte> body)
    {
        if (!LanAddress.IsAllowed(source)) return TapVerdict.Deny(DenyReason.NonLanSource);
        if (!TapParser.TryParse(body, out var tap)) return TapVerdict.Deny(DenyReason.BadFormat);

        long now = _time.GetUtcNow().ToUnixTimeSeconds();
        Int128 diff = (Int128)now - tap.Ts;
        if (diff > MaxSkewSeconds || diff < -MaxSkewSeconds)
            return TapVerdict.Deny(DenyReason.ClockSkew, tap.Tag, tap.Ts);

        lock (_gate)
        {
            long? last = replay.GetLastTs(tap.Tag);
            if (last is not null && tap.Ts <= last.Value)
                return TapVerdict.Deny(DenyReason.Replay, tap.Tag, tap.Ts);

            string? secret = secrets.GetSecret();
            if (string.IsNullOrEmpty(secret))
                return TapVerdict.Deny(DenyReason.NotPaired, tap.Tag, tap.Ts);
            if (!TapSignature.Verify(tap.Tag, tap.TsText, secret, tap.Sig))
                return TapVerdict.Deny(DenyReason.BadSignature, tap.Tag, tap.Ts);

            bool learn = false;
            if (!state.IsTagRegistered(tap.Tag))
            {
                if (!state.LearningMode) return TapVerdict.Deny(DenyReason.UnknownTag, tap.Tag, tap.Ts);
                learn = true;
            }

            if (state.Paused) return TapVerdict.Deny(DenyReason.Paused, tap.Tag, tap.Ts);

            replay.SetLastTs(tap.Tag, tap.Ts);
            return new TapVerdict(true, DenyReason.None, tap.Tag, tap.Ts, learn);
        }
    }
}

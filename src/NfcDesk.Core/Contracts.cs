// Shared contracts. Every component codes against these; do not change signatures without review.
using System.Net;

namespace NfcDesk.Core;

/// <summary>Why a tap was denied. Logged only — never sent to the client.</summary>
public enum DenyReason
{
    None = 0,
    RateLimited,    // pre-check: >10 req/s from this IP
    NonLanSource,   // check 1
    BadFormat,      // check 2 (invalid JSON, bad tag, bad ts, missing/extra fields, body too large)
    ClockSkew,      // check 3
    Replay,         // check 4
    NotPaired,      // check 5: no secret configured
    BadSignature,   // check 5
    UnknownTag,     // check 6
    Paused,         // agent paused by user (checked after a fully valid request; nothing runs)
}

/// <summary>Result of verifying one tap.</summary>
/// <param name="Accepted">True → respond 200 ok.</param>
/// <param name="Tag">Parsed tag if format check passed, else null (raw tag text is never trusted for logging beyond a sanitized prefix).</param>
/// <param name="LearnCandidate">True when signature valid, tag unknown and Learning Mode on. Accepted is also true in that case.</param>
public sealed record TapVerdict(bool Accepted, DenyReason Reason, string? Tag, long Ts, bool LearnCandidate)
{
    public static TapVerdict Deny(DenyReason r, string? tag = null, long ts = 0) => new(false, r, tag, ts, false);
}

/// <summary>Provides the pairing secret as the hex string shown to the user (what the phone hashes). Null if not paired.</summary>
public interface ISecretProvider
{
    string? GetSecret();
}

/// <summary>Persisted "last accepted ts per tag" for replay protection.</summary>
public interface IReplayStore
{
    /// <summary>Returns last accepted ts for tag, or null if none.</summary>
    long? GetLastTs(string tag);
    /// <summary>Records ts as accepted for tag and persists it.</summary>
    void SetLastTs(string tag, long ts);
}

/// <summary>Tag registry + runtime switches the verifier needs.</summary>
public interface IAgentState
{
    bool IsTagRegistered(string tag);
    bool LearningMode { get; }
    bool Paused { get; }
}

/// <summary>One line in the activity log.</summary>
public sealed record TapLogEntry(DateTimeOffset Time, IPAddress? Source, string? Tag, bool Accepted, DenyReason Reason, string? Detail = null);

public interface IActivityLog
{
    void LogTap(TapLogEntry entry);
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

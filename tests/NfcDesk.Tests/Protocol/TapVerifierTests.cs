using System.Net;
using System.Security.Cryptography;
using System.Text;
using NfcDesk.Core;
using NfcDesk.Core.Protocol;

namespace NfcDesk.Tests.Protocol;

public class TapVerifierTests
{
    private const string Tag = "desk-focus-01";
    private const string PinnedDigest = "def9230702595cc8fb1e1417409baf21add82671450fbbefa7b5197417810c54";

    [Fact]
    public void ValidTapAccepted()
    {
        var r = new Rig();
        var v = r.Send(Rig.Body(Tag, Rig.Now));
        Assert.True(v.Accepted);
        Assert.Equal(DenyReason.None, v.Reason);
        Assert.Equal(Tag, v.Tag);
        Assert.Equal(Rig.Now, v.Ts);
        Assert.False(v.LearnCandidate);
        Assert.Equal(Rig.Now, r.Replay.Map[Tag]);
    }

    [Fact]
    public void IPhoneCompatibility_NumberAndString()
    {
        const string secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        const long ts = 1763441000;
        string phoneInput = $"{Tag}|{ts}|{secret}";
        string sig = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phoneInput))).ToLowerInvariant();
        Assert.Equal(PinnedDigest, sig);

        var r1 = new Rig();
        Assert.True(r1.Send($"{{\"tag\":\"{Tag}\",\"ts\":{ts},\"sig\":\"{sig}\"}}").Accepted);
        var r2 = new Rig();
        Assert.True(r2.Send($"{{\"tag\":\"{Tag}\",\"ts\":\"{ts}\",\"sig\":\"{sig}\"}}").Accepted);
    }

    [Fact]
    public void WrongSecret_BadSignature()
    {
        var r = new Rig();
        var v = r.Send(Rig.Body(Tag, Rig.Now, secret: "ff" + Rig.Secret[2..]));
        Assert.Equal(DenyReason.BadSignature, v.Reason);
    }

    [Fact]
    public void TamperedTagOrTs_BadSignature()
    {
        var r = new Rig();
        r.State.Registered.Add("desk-focus-02");
        string sig = Rig.Sig(Tag, Rig.Now.ToString());
        Assert.Equal(DenyReason.BadSignature, r.Send(Rig.Body("desk-focus-02", Rig.Now, sig)).Reason);
        Assert.Equal(DenyReason.BadSignature, r.Send(Rig.Body(Tag, Rig.Now + 1, sig)).Reason);
    }

    [Fact]
    public void UppercaseSigAccepted()
    {
        var r = new Rig();
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now, Rig.Sig(Tag, Rig.Now.ToString()).ToUpperInvariant())).Accepted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz23456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void BadSigShape_BadSignature(string sig)
    {
        var r = new Rig();
        Assert.Equal(DenyReason.BadSignature, r.Send(Rig.Body(Tag, Rig.Now, sig)).Reason);
    }

    [Fact]
    public void SigTooLong_BadSignature()
    {
        var r = new Rig();
        Assert.Equal(DenyReason.BadSignature, r.Send(Rig.Body(Tag, Rig.Now, Rig.Sig(Tag, Rig.Now.ToString()) + "00")).Reason);
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("desk-focus-01", true)]
    [InlineData("", false)]
    [InlineData("Desk", false)]
    [InlineData("a_b", false)]
    [InlineData("a b", false)]
    [InlineData("café", false)]
    [InlineData("tag\\n", false)]
    [InlineData("a.b", false)]
    [InlineData("a/b", false)]
    [InlineData("..", false)]
    public void TagRegex(string tag, bool ok)
    {
        Assert.Equal(ok, TapParser.TryParse(Encoding.UTF8.GetBytes($"{{\"tag\":\"{tag}\",\"ts\":1,\"sig\":\"x\"}}"), out _));
    }

    [Fact]
    public void TagLengthLimits()
    {
        Assert.True(TapParser.TryParse(Encoding.UTF8.GetBytes($"{{\"tag\":\"{new string('a', 64)}\",\"ts\":1,\"sig\":\"x\"}}"), out _));
        Assert.False(TapParser.TryParse(Encoding.UTF8.GetBytes($"{{\"tag\":\"{new string('a', 65)}\",\"ts\":1,\"sig\":\"x\"}}"), out _));
    }

    [Theory]
    [InlineData("1763441000", true)]
    [InlineData("\"1763441000\"", true)]
    [InlineData("0", true)]
    [InlineData("1763441000.0", false)]
    [InlineData("1763441000.5", false)]
    [InlineData("\"1763441000.0\"", false)]
    [InlineData("1.7e9", false)]
    [InlineData("-5", false)]
    [InlineData("\"-5\"", false)]
    [InlineData("01", false)]
    [InlineData("\"01\"", false)]
    [InlineData("\" 123\"", false)]
    [InlineData("\"+123\"", false)]
    [InlineData("\"123abc\"", false)]
    [InlineData("\"123\\n\"", false)]
    [InlineData("\"\"", false)]
    [InlineData("null", false)]
    [InlineData("true", false)]
    [InlineData("{}", false)]
    [InlineData("99999999999999999999", false)]
    [InlineData("\"9999999999999999999\"", false)]
    public void TsForms(string tsJson, bool ok)
    {
        Assert.Equal(ok, TapParser.TryParse(Encoding.UTF8.GetBytes($"{{\"tag\":\"a\",\"ts\":{tsJson},\"sig\":\"x\"}}"), out var t));
        if (ok) Assert.Equal(tsJson.Trim('"'), t.TsText);
    }

    [Theory]
    [InlineData("{\"tag\":\"a\",\"ts\":1,\"sig\":\"x\",\"extra\":1}")]
    [InlineData("{\"tag\":\"a\",\"ts\":1}")]
    [InlineData("{\"tag\":\"a\",\"tag\":\"a\",\"ts\":1,\"sig\":\"x\"}")]
    [InlineData("{\"Tag\":\"a\",\"ts\":1,\"sig\":\"x\"}")]
    [InlineData("[1]")]
    [InlineData("\"str\"")]
    [InlineData("{\"tag\":\"a\",\"ts\":1,\"sig\":\"x\"} junk")]
    [InlineData("{\"tag\":\"a\",\"ts\":1,\"sig\":\"x\"}{}")]
    [InlineData("{\"tag\":\"a\",\"ts\":1,\"sig\":\"x\",}")]
    [InlineData("{\"tag\":\"a\",\"ts\":1,\"sig\":5}")]
    [InlineData("{\"tag\":\"a\",\"ts\":1,\"sig\":\"x\"")]
    [InlineData("{/*c*/\"tag\":\"a\",\"ts\":1,\"sig\":\"x\"}")]
    [InlineData("not json")]
    [InlineData("")]
    public void BadStructure_BadFormat(string json)
    {
        var r = new Rig();
        Assert.Equal(DenyReason.BadFormat, r.Send(json).Reason);
    }

    [Fact]
    public void BodyOver1024_BadFormat()
    {
        var r = new Rig();
        string json = $"{{\"tag\":\"a\",\"ts\":1,\"sig\":\"{new string('a', 1100)}\"}}";
        Assert.Equal(DenyReason.BadFormat, r.Send(json).Reason);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(-30, true)]
    [InlineData(31, false)]
    [InlineData(-31, false)]
    public void ClockSkew(int offset, bool ok)
    {
        var r = new Rig();
        var v = r.Send(Rig.Body(Tag, Rig.Now + offset));
        Assert.Equal(ok, v.Accepted);
        if (!ok) Assert.Equal(DenyReason.ClockSkew, v.Reason);
    }

    [Fact]
    public void ExtremeTs_NoOverflow()
    {
        var r = new Rig();
        Assert.Equal(DenyReason.ClockSkew, r.Send($"{{\"tag\":\"a\",\"ts\":{long.MaxValue},\"sig\":\"x\"}}").Reason);
    }

    [Fact]
    public void Replay_Rules()
    {
        var r = new Rig();
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now)).Accepted);
        Assert.Equal(DenyReason.Replay, r.Send(Rig.Body(Tag, Rig.Now)).Reason);
        Assert.Equal(DenyReason.Replay, r.Send(Rig.Body(Tag, Rig.Now - 1)).Reason);
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now + 1)).Accepted);
    }

    [Fact]
    public void DeniedTapDoesNotAdvanceLastTs()
    {
        var r = new Rig();
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now)).Accepted);
        Assert.Equal(DenyReason.BadSignature, r.Send(Rig.Body(Tag, Rig.Now + 5, new string('0', 64))).Reason);
        Assert.Equal(Rig.Now, r.Replay.Map[Tag]);
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now + 1)).Accepted);
    }

    [Fact]
    public void Order_NonLanBeforeFormat()
    {
        var r = new Rig();
        Assert.Equal(DenyReason.NonLanSource, r.Send("garbage", IPAddress.Parse("8.8.8.8")).Reason);
    }

    [Fact]
    public void Order_FormatBeforeSkew()
    {
        var r = new Rig();
        Assert.Equal(DenyReason.BadFormat, r.Send("{\"tag\":\"A\",\"ts\":5,\"sig\":\"x\"}").Reason);
    }

    [Fact]
    public void Order_ReplayBeforeSignature()
    {
        var r = new Rig();
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now)).Accepted);
        Assert.Equal(DenyReason.Replay, r.Send(Rig.Body(Tag, Rig.Now, new string('0', 64))).Reason);
    }

    [Fact]
    public void UnknownTag()
    {
        var r = new Rig();
        var v = r.Send(Rig.Body("other", Rig.Now));
        Assert.Equal(DenyReason.UnknownTag, v.Reason);
        Assert.False(r.Replay.Map.ContainsKey("other"));
    }

    [Fact]
    public void UnknownTag_Learning_Accepted()
    {
        var r = new Rig();
        r.State.LearningMode = true;
        var v = r.Send(Rig.Body("other", Rig.Now));
        Assert.True(v.Accepted);
        Assert.True(v.LearnCandidate);
    }

    [Fact]
    public void UnknownTag_Learning_BadSig_NeverLearned()
    {
        var r = new Rig();
        r.State.LearningMode = true;
        var v = r.Send(Rig.Body("other", Rig.Now, new string('0', 64)));
        Assert.Equal(DenyReason.BadSignature, v.Reason);
        Assert.False(v.LearnCandidate);
    }

    [Fact]
    public void Paused_DeniedAndLastTsNotAdvanced()
    {
        var r = new Rig();
        r.State.Paused = true;
        Assert.Equal(DenyReason.Paused, r.Send(Rig.Body(Tag, Rig.Now)).Reason);
        Assert.False(r.Replay.Map.ContainsKey(Tag));
        r.State.Paused = false;
        Assert.True(r.Send(Rig.Body(Tag, Rig.Now)).Accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NotPaired(string? secret)
    {
        var r = new Rig();
        r.Secrets.Secret = secret;
        Assert.Equal(DenyReason.NotPaired, r.Send(Rig.Body(Tag, Rig.Now)).Reason);
    }

    [Fact]
    public void ConcurrentIdenticalRequests_OnlyOneAccepted()
    {
        var r = new Rig();
        var body = Rig.Body(Tag, Rig.Now);
        int accepted = 0;
        Parallel.For(0, 64, _ => { if (r.Verifier.Verify(Rig.Lan, body).Accepted) Interlocked.Increment(ref accepted); });
        Assert.Equal(1, accepted);
    }
}

using System.Security.Cryptography;
using System.Text;

namespace NfcDesk.Core.Protocol;

public static class TapSignature
{
    public static string Compute(string tag, string tsText, string secret)
        => Convert.ToHexString(Hash(tag, tsText, secret)).ToLowerInvariant();

    public static bool Verify(string tag, string tsText, string secret, string sig)
    {
        if (sig is null || sig.Length != 64) return false;
        foreach (char c in sig)
        {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        byte[] provided = Convert.FromHexString(sig);
        return CryptographicOperations.FixedTimeEquals(provided, Hash(tag, tsText, secret));
    }

    private static byte[] Hash(string tag, string tsText, string secret)
        => SHA256.HashData(Encoding.UTF8.GetBytes($"{tag}|{tsText}|{secret}"));
}

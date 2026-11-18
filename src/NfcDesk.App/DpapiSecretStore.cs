using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NfcDesk.Core;
using NfcDesk.Core.Config;

namespace NfcDesk.App;

[SupportedOSPlatform("windows")]
public sealed class DpapiSecretStore : ISecretProvider
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NFCDesk.pairing.v1");

    private readonly string _file;
    private readonly IActivityLog? _log;
    private readonly object _lock = new();
    private string? _cache;
    private bool _loaded;

    public DpapiSecretStore(AppPaths paths, IActivityLog? log = null)
    {
        _file = paths.SecretFile;
        _log = log;
    }

    public bool HasSecret => !string.IsNullOrEmpty(GetSecret());

    public string? GetSecret()
    {
        lock (_lock)
        {
            if (_loaded) return _cache;
            _loaded = true;
            _cache = ReadFile();
            return _cache;
        }
    }

    public string Regenerate()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllBytes(tmp, protectedBytes);
            File.Move(tmp, _file, overwrite: true);
            _cache = secret;
            _loaded = true;
        }
        return secret;
    }

    private string? ReadFile()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var data = File.ReadAllBytes(_file);
            var plain = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            var s = Encoding.UTF8.GetString(plain).Trim();
            if (s.Length == 64 && s.All(Uri.IsHexDigit)) return s.ToLowerInvariant();
            _log?.Error("Pairing secret file has unexpected format; treating as not paired");
            return null;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            _log?.Error("Pairing secret could not be read; treating as not paired", ex);
            return null;
        }
    }
}

using System.Net;

namespace NfcDesk.Core.Net;

public static class LanAddress
{
    public static bool IsAllowed(IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return IPAddress.IPv6Loopback.Equals(ip);

        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        Span<byte> b = stackalloc byte[4];
        if (!ip.TryWriteBytes(b, out _)) return false;
        return b[0] == 10
            || b[0] == 127
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }
}

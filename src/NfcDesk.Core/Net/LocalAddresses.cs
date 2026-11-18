using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace NfcDesk.Core.Net;

public static class LocalAddresses
{
    /// <summary>Private (RFC1918) IPv4 addresses of up, non-loopback, non-tunnel interfaces.</summary>
    public static IReadOnlyList<IPAddress> GetLanIPv4()
    {
        var result = new List<IPAddress>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (var nic in nics)
        {
            try
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    var ip = ua.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ip) || ip.GetAddressBytes()[0] == 127) continue;
                    if (!LanAddress.IsAllowed(ip)) continue;
                    result.Add(ip);
                }
            }
            catch (NetworkInformationException) { }
        }

        return result.DistinctBy(i => i.ToString())
                     .OrderBy(i => i.GetAddressBytes(), Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))
                     .ToList();
    }
}

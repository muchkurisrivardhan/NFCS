using System.Net;
using NfcDesk.Core.Net;

namespace NfcDesk.Tests.Server;

public class LocalAddressesTests
{
    [Fact]
    public void NeverReturnsLoopbackOrNonPrivate()
    {
        var list = LocalAddresses.GetLanIPv4();
        foreach (var ip in list)
        {
            Assert.False(IPAddress.IsLoopback(ip));
            Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, ip.AddressFamily);
            Assert.NotEqual(127, ip.GetAddressBytes()[0]);
            Assert.True(LanAddress.IsAllowed(ip));
        }
        Assert.Equal(list.Count, list.Select(i => i.ToString()).Distinct().Count());
    }
}

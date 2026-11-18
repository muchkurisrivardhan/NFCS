using System.Net;
using NfcDesk.Core.Net;

namespace NfcDesk.Tests.Net;

public class LanAddressTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.50")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:192.168.1.50")]
    public void Allowed(string ip) => Assert.True(LanAddress.IsAllowed(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("2001:db8::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:8.8.8.8")]
    public void Denied(string ip) => Assert.False(LanAddress.IsAllowed(IPAddress.Parse(ip)));

    [Fact]
    public void NullDenied() => Assert.False(LanAddress.IsAllowed(null));
}

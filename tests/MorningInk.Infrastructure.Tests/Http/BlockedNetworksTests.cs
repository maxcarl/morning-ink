using System.Net;
using MorningInk.Infrastructure.Http;

namespace MorningInk.Infrastructure.Tests.Http;

public class BlockedNetworksTests
{
    [Theory]
    [InlineData("127.0.0.1")] // loopback
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.178.1")] // typical home router
    [InlineData("169.254.169.254")] // cloud metadata, link-local
    [InlineData("100.64.0.1")] // carrier-grade NAT, also Tailscale
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")] // multicast
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")] // unique local
    [InlineData("::ffff:192.168.0.1")] // IPv4-mapped
    [InlineData("2002:c0a8:0101::1")] // 6to4 wrapping 192.168.1.1
    [InlineData("ff02::1")]
    public void Internal_addresses_are_blocked(string address) =>
        Assert.True(BlockedNetworks.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("172.15.255.255")] // just below 172.16.0.0/12
    [InlineData("172.32.0.1")] // just above it
    [InlineData("::ffff:1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2002:0101:0101::1")] // 6to4 wrapping a public address
    public void Public_addresses_are_allowed(string address) =>
        Assert.False(BlockedNetworks.IsBlocked(IPAddress.Parse(address)));
}

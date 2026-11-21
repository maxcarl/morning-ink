using System.Net;
using System.Net.Sockets;

namespace MorningInk.Infrastructure.Http;

/// <summary>Address ranges Morning Ink never connects to unless the host is allowlisted.</summary>
internal static class BlockedNetworks
{
    private static readonly IPNetwork[] BlockedV4 =
    [
        IPNetwork.Parse("0.0.0.0/8"), // "this network"
        IPNetwork.Parse("10.0.0.0/8"), // private
        IPNetwork.Parse("100.64.0.0/10"), // carrier-grade NAT, also used by Tailscale
        IPNetwork.Parse("127.0.0.0/8"), // loopback
        IPNetwork.Parse("169.254.0.0/16"), // link-local, includes cloud metadata 169.254.169.254
        IPNetwork.Parse("172.16.0.0/12"), // private
        IPNetwork.Parse("192.0.0.0/24"), // IETF protocol assignments
        IPNetwork.Parse("192.168.0.0/16"), // private
        IPNetwork.Parse("198.18.0.0/15"), // benchmarking
        IPNetwork.Parse("224.0.0.0/4"), // multicast
        IPNetwork.Parse("240.0.0.0/4"), // reserved, includes broadcast
    ];

    // Only global unicast is reachable; this excludes loopback, unique local (fc00::/7), link-local and multicast.
    private static readonly IPNetwork GlobalUnicastV6 = IPNetwork.Parse("2000::/3");

    private static readonly IPNetwork[] BlockedWithinGlobalV6 =
    [
        IPNetwork.Parse("2001::/32"), // Teredo tunnels, embed an obfuscated IPv4 address
        IPNetwork.Parse("2001:db8::/32"), // documentation
    ];

    // 6to4 embeds an IPv4 address in bytes 2 to 5; that address decides.
    private static readonly IPNetwork SixToFour = IPNetwork.Parse("2002::/16");

    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return BlockedV4.Any(network => network.Contains(address));
        }

        if (SixToFour.Contains(address))
        {
            return IsBlocked(new IPAddress(address.GetAddressBytes()[2..6]));
        }

        // ponytail: NAT64 (64:ff9b::/96) is outside 2000::/3 and therefore blocked as a whole;
        // inspect the embedded IPv4 address if IPv6-only hosts need to reach IPv4-only feeds.
        return !GlobalUnicastV6.Contains(address) || BlockedWithinGlobalV6.Any(network => network.Contains(address));
    }
}

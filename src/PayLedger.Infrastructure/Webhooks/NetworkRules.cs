using System.Net;
using System.Net.Sockets;

namespace PayLedger.Infrastructure.Webhooks;

/// <summary>Decides which IP addresses a webhook may be sent to.</summary>
public static class NetworkRules
{
    private static readonly (IPAddress Network, int PrefixLength)[] BlockedV4 =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4),
    ];

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !BlockedV4.Any(b => InNetwork(address, b.Network, b.PrefixLength));
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || address.IsIPv6UniqueLocal || address.IsIPv6Teredo || address.IsIPv4MappedToIPv6)
        {
            return false;
        }

        // 64:ff9b::/96 (NAT64) can reach IPv4 private ranges; 2001:db8::/32 is documentation only.
        var bytes = address.GetAddressBytes();
        var isNat64 = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xff && bytes[3] == 0x9b;
        var isDocumentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8;
        return !isNat64 && !isDocumentation;
    }

    private static bool InNetwork(IPAddress address, IPAddress network, int prefixLength)
    {
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        for (var bit = 0; bit < prefixLength; bit++)
        {
            var mask = (byte)(0x80 >> (bit % 8));
            if ((a[bit / 8] & mask) != (n[bit / 8] & mask))
            {
                return false;
            }
        }

        return true;
    }
}

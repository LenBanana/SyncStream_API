using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace SyncStreamAPI.Net;

/// <summary>
///     Whether an address belongs to somewhere the server should never be talked into reaching.
/// </summary>
/// <remarks>
///     The server fetches URLs that clients supply, to read a page title and to download media. Without this,
///     any of those became a request issued from inside the network: the cloud metadata service on
///     169.254.169.254, a database on localhost, a router's admin page, another container on the bridge network.
///     The page title comes back to the room, so it was a read channel as well as a way to reach things.
/// </remarks>
public static class PrivateAddresses
{
    public static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // A v4 address wearing a v6 costume, which is how ::ffff:127.0.0.1 slips past a v6-only check.
            if (address.IsIPv4MappedToIPv6) return IsPrivate(address.MapToIPv4());

            return address.IsIPv6LinkLocal
                   || address.IsIPv6SiteLocal
                   || address.IsIPv6Multicast
                   || address.Equals(IPAddress.IPv6Any)
                   || IsUniqueLocalV6(address);
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return true;

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true, // 0.0.0.0/8, which includes "this host"
            10 => true, // 10.0.0.0/8
            127 => true, // covered above, kept so the table reads completely
            169 when octets[1] == 254 => true, // 169.254.0.0/16 link-local, and the metadata address with it
            172 when octets[1] >= 16 && octets[1] <= 31 => true, // 172.16.0.0/12
            192 when octets[1] == 168 => true, // 192.168.0.0/16
            100 when octets[1] >= 64 && octets[1] <= 127 => true, // 100.64.0.0/10 carrier-grade NAT
            198 when octets[1] == 18 || octets[1] == 19 => true, // 198.18.0.0/15 benchmarking
            >= 224 => true, // multicast and reserved, 255.255.255.255 among them
            _ => false
        };
    }

    /// <summary>fc00::/7, the v6 equivalent of the private ranges.</summary>
    private static bool IsUniqueLocalV6(IPAddress address)
    {
        return (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }

    /// <summary>The addresses of a resolved host that the server is willing to connect to.</summary>
    public static IPAddress[] PublicOnly(IPAddress[] resolved)
    {
        return resolved.Where(address => !IsPrivate(address)).ToArray();
    }
}

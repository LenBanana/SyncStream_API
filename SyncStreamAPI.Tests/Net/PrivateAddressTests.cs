using System.Net;
using SyncStreamAPI.Net;
using Xunit;

namespace SyncStreamAPI.Tests.Net;

/// <summary>
///     Which addresses the server refuses to be talked into reaching.
/// </summary>
/// <remarks>
///     The server fetches URLs that clients supply, to read a page title when a video is added and to download
///     media. Every one of those was a plain fetch, so each was a way to make the server issue a request from
///     inside its own network, and the title came back into the room as the video's name, which made it a way to
///     read the answer as well as to reach it.
/// </remarks>
public class PrivateAddressTests
{
    [Theory]
    // The one that matters most on a hosted box: the cloud metadata service, which hands out credentials.
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.0.1")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    // A v4 address wearing a v6 costume, which is how loopback slips past a check that only knows v4 shapes.
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    public void An_address_inside_the_network_is_refused(string address)
    {
        Assert.True(PrivateAddresses.IsPrivate(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.32.0.1")] // just outside 172.16/12
    [InlineData("172.15.255.255")] // just below it
    [InlineData("192.169.0.1")] // just outside 192.168/16
    [InlineData("100.128.0.1")] // just outside the carrier-grade range
    [InlineData("2606:4700::1111")]
    public void An_ordinary_public_address_is_allowed(string address)
    {
        Assert.False(PrivateAddresses.IsPrivate(IPAddress.Parse(address)));
    }

    [Fact]
    public void A_host_that_resolves_to_both_keeps_only_the_public_addresses()
    {
        // A name under an attacker's control can answer with a public address and a private one together, hoping
        // whatever connects picks the wrong one.
        var mixed = new[]
        {
            IPAddress.Parse("8.8.8.8"),
            IPAddress.Parse("127.0.0.1"),
            IPAddress.Parse("169.254.169.254")
        };

        var allowed = PrivateAddresses.PublicOnly(mixed);

        Assert.Single(allowed);
        Assert.Equal(IPAddress.Parse("8.8.8.8"), allowed[0]);
    }

    [Fact]
    public void A_host_that_resolves_only_inside_the_network_leaves_nothing_to_connect_to()
    {
        var inside = new[] { IPAddress.Parse("10.1.2.3"), IPAddress.Parse("127.0.0.1") };

        Assert.Empty(PrivateAddresses.PublicOnly(inside));
    }
}

using System.Linq;
using SyncStreamAPI.ServerData;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>
///     Ownership of SFU resources. Producer ids are announced to everyone in an SFU room, so the hub has to know
///     whose they are: it used to close any producer it was named, and any viewer could end the stream.
/// </summary>
public class SfuPeerRegistryTests
{
    private const string Stage = "stage:room";

    [Fact]
    public void A_transport_cannot_be_added_before_joining()
    {
        var registry = new SfuPeerRegistry();

        Assert.False(registry.AddTransport("a", Stage, "t1", sending: false));
        Assert.False(registry.OwnsTransport("a", Stage, "t1"));
    }

    [Fact]
    public void Only_the_creator_owns_a_transport_and_only_in_its_room()
    {
        var registry = new SfuPeerRegistry();
        registry.Join("a", Stage);
        registry.Join("b", Stage);
        registry.AddTransport("a", Stage, "t1", sending: true);

        Assert.True(registry.OwnsSendTransport("a", Stage, "t1"));
        Assert.False(registry.OwnsRecvTransport("a", Stage, "t1"));
        Assert.False(registry.OwnsTransport("b", Stage, "t1"));
        Assert.False(registry.OwnsTransport("a", "voice:room", "t1"));
    }

    [Fact]
    public void Another_connection_cannot_remove_a_producer()
    {
        var registry = new SfuPeerRegistry();
        registry.Join("streamer", Stage);
        registry.Join("viewer", Stage);
        registry.AddProducer("streamer", Stage, "p1");

        Assert.False(registry.RemoveProducer("viewer", Stage, "p1"));
        Assert.True(registry.RemoveProducer("streamer", Stage, "p1"));
        Assert.False(registry.RemoveProducer("streamer", Stage, "p1"));
    }

    [Fact]
    public void Producers_are_capped_per_room()
    {
        var registry = new SfuPeerRegistry();
        registry.Join("a", Stage);
        for (var i = 0; i < SfuPeerRegistry.MaxProducersPerRoom; i++)
        {
            Assert.True(registry.CanAddProducer("a", Stage));
            registry.AddProducer("a", Stage, $"p{i}");
        }

        Assert.False(registry.CanAddProducer("a", Stage));
    }

    [Fact]
    public void Leaving_hands_back_everything_held_in_that_room_only()
    {
        var registry = new SfuPeerRegistry();
        registry.Join("a", Stage);
        registry.Join("a", "voice:room");
        registry.AddTransport("a", Stage, "send", sending: true);
        registry.AddTransport("a", Stage, "recv", sending: false);
        registry.AddProducer("a", Stage, "p1");
        registry.AddTransport("a", "voice:room", "voice-recv", sending: false);

        var released = registry.Leave("a", Stage);

        Assert.NotNull(released);
        Assert.Equal(new[] { "recv", "send" }, released!.Transports.OrderBy(t => t));
        Assert.Equal(new[] { "p1" }, released.Producers);
        Assert.False(registry.HasJoined("a", Stage));
        Assert.True(registry.OwnsRecvTransport("a", "voice:room", "voice-recv"));
        Assert.Null(registry.Leave("a", Stage));
    }

    [Fact]
    public void Leaving_by_filter_releases_each_matching_room_once()
    {
        var registry = new SfuPeerRegistry();
        registry.Join("a", "stage:one");
        registry.Join("a", "voice:one");
        registry.Join("a", "stage:two");

        var released = registry.LeaveWhere("a", room => room.EndsWith(":one"));

        Assert.Equal(new[] { "stage:one", "voice:one" }, released.Select(r => r.SfuRoomId).OrderBy(r => r));
        Assert.Empty(registry.LeaveWhere("a", room => room.EndsWith(":one")));
        Assert.True(registry.HasJoined("a", "stage:two"));
    }
}

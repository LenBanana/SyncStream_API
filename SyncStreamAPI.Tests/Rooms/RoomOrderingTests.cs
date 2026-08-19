using System.Linq;
using SyncStreamAPI.Models;
using Xunit;

namespace SyncStreamAPI.Tests.Rooms;

/// <summary>
///     The lobby lists rooms in the order they were created.
/// </summary>
/// <remarks>
///     The room store used to be a queue, which preserved insertion order as a side effect. Keying rooms by
///     id for lookup lost that, because a dictionary hands back its values in hash order, so the lobby order
///     became arbitrary and a newly created room appeared wherever it happened to land rather than at the
///     end. Ordering is explicit now, and pinned here so it cannot quietly depend on a collection again.
/// </remarks>
public class RoomOrderingTests
{
    [Fact]
    public void A_room_created_later_sorts_after_one_created_earlier()
    {
        var first = new Room("First", "first", true, false);
        var second = new Room("Second", "second", true, false);
        var third = new Room("Third", "third", true, false);

        var ordered = new[] { third, first, second }
            .OrderBy(room => room.Sequence)
            .Select(room => room.uniqueId)
            .ToArray();

        Assert.Equal(new[] { "first", "second", "third" }, ordered);
    }

    [Fact]
    public void Every_room_gets_its_own_position()
    {
        // Assigned from an interlocked counter rather than a timestamp, because rooms created in a loop can
        // share a timestamp and then sort unpredictably against each other.
        var rooms = Enumerable.Range(0, 50)
            .Select(index => new Room($"Room {index}", $"room-{index}", true, false))
            .ToList();

        var sequences = rooms.Select(room => room.Sequence).ToList();

        Assert.Equal(sequences.Count, sequences.Distinct().Count());
        Assert.Equal(sequences.OrderBy(value => value).ToList(), sequences);
    }
}

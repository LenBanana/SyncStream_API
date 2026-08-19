using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.Collections;
using Xunit;

namespace SyncStreamAPI.Tests.Rooms;

/// <summary>
///     The properties the room member collection exists to provide.
/// </summary>
public class MemberListTests
{
    private static Member NewMember(string username)
    {
        return new Member { username = username, ConnectionId = $"conn-{username}" };
    }

    [Fact]
    public void Order_is_preserved_because_it_is_the_turn_order()
    {
        var members = new MemberList();
        foreach (var name in new[] { "first", "second", "third" }) members.Add(NewMember(name));

        Assert.Equal(new[] { "first", "second", "third" }, members.Select(m => m.username).ToArray());
        Assert.Equal("second", members[1]!.username);
        Assert.Equal(1, members.FindIndex(m => m.username == "second"));
    }

    [Fact]
    public void Indexing_a_position_that_has_gone_returns_null_rather_than_throwing()
    {
        // Callers index positions they found a moment earlier, and a member can leave in between. The plain
        // List this replaced threw ArgumentOutOfRangeException in that window.
        var members = new MemberList();
        members.Add(NewMember("only"));

        Assert.NotNull(members[0]);
        Assert.Null(members[1]);
        Assert.Null(members[-1]);

        members.RemoveAt(5); // no such position: a no-op, not a throw
        Assert.Equal(1, members.Count);
    }

    [Fact]
    public async Task Iterating_while_members_join_and_leave_does_not_throw()
    {
        // This is the crash the class exists to prevent: broadcasts enumerate the member list while hub
        // invocations add and remove from it, which on a plain List raises "collection was modified".
        var members = new MemberList();
        for (var i = 0; i < 20; i++) members.Add(NewMember($"seed{i}"));

        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var failures = new List<Exception>();

        var writer = Task.Run(() =>
        {
            var counter = 0;
            while (!stop.IsCancellationRequested)
                try
                {
                    var joining = NewMember($"churn{counter++}");
                    members.Add(joining);
                    members.Remove(joining);
                }
                catch (Exception error)
                {
                    lock (failures) failures.Add(error);
                    return;
                }
        });

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                try
                {
                    // The shapes broadcasts actually use.
                    var names = members.Select(member => member?.username).ToList();
                    Assert.True(names.Count >= 20);
                    _ = members.Count;
                    members.ForEach(_ => { });
                }
                catch (Exception error)
                {
                    lock (failures) failures.Add(error);
                    return;
                }
        });

        await Task.WhenAll(writer, reader);

        Assert.True(failures.Count == 0,
            "Concurrent read and write failed: " + string.Join("; ", failures.Select(f => f.Message)));
    }

    [Fact]
    public void A_snapshot_taken_before_a_change_is_unaffected_by_it()
    {
        var members = new MemberList();
        members.Add(NewMember("stays"));

        var snapshot = members.Snapshot;
        members.Add(NewMember("arrives-later"));

        // Readers hold a snapshot for the length of a broadcast, so it must not shift underneath them.
        Assert.Single(snapshot);
        Assert.Equal(2, members.Count);
    }
}

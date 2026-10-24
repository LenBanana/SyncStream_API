using System;
using SyncStreamAPI.Helper.Streaming;
using Xunit;

namespace SyncStreamAPI.Tests.Streaming;

/// <summary>
///     The streaming server lets a play through on the strength of a ticket alone, so a ticket has to say
///     exactly what it was issued for and be impossible to make up or alter.
/// </summary>
public class LivePlayTicketTests
{
    private static readonly DateTimeOffset Later = DateTimeOffset.UtcNow.AddMinutes(2);

    [Fact]
    public void A_ticket_reads_back_as_issued()
    {
        var tickets = new LivePlayTickets();
        var token = tickets.Issue("share-0123abc", 42, Later);

        Assert.True(tickets.TryRead(token, out var ticket));
        Assert.Equal("share-0123abc", ticket.Stream);
        Assert.Equal(42, ticket.UserId);
        Assert.Equal(Later.ToUnixTimeSeconds(), ticket.ExpiresAt.ToUnixTimeSeconds());
    }

    [Fact]
    public void A_guest_ticket_names_no_account()
    {
        var tickets = new LivePlayTickets();
        Assert.True(tickets.TryRead(tickets.Issue("alice", null, Later), out var ticket));
        Assert.Null(ticket.UserId);
    }

    [Fact]
    public void A_stream_name_with_dots_and_other_characters_survives()
    {
        var tickets = new LivePlayTickets();
        Assert.True(tickets.TryRead(tickets.Issue("Ä.b c/d", 1, Later), out var ticket));
        Assert.Equal("Ä.b c/d", ticket.Stream);
    }

    [Fact]
    public void An_expired_ticket_still_reads_so_the_end_of_its_play_can_be_recorded()
    {
        var tickets = new LivePlayTickets();
        var past = DateTimeOffset.UtcNow.AddMinutes(-30);
        Assert.True(tickets.TryRead(tickets.Issue("alice", 1, past), out var ticket));
        Assert.True(ticket.ExpiresAt < DateTimeOffset.UtcNow);
    }

    [Fact]
    public void A_ticket_for_one_stream_cannot_be_turned_into_one_for_another()
    {
        var tickets = new LivePlayTickets();
        var token = tickets.Issue("alice", 1, Later);
        var otherStream = tickets.Issue("bob", 1, Later);

        // The stream is the first part of the signed payload; lift bob's onto alice's signature.
        var forged = otherStream.Split('.')[0] + token[token.IndexOf('.')..];
        Assert.False(tickets.TryRead(forged, out _));
    }

    [Fact]
    public void Extending_a_ticket_breaks_its_signature()
    {
        var tickets = new LivePlayTickets();
        var parts = tickets.Issue("alice", 1, Later).Split('.');
        parts[2] = DateTimeOffset.UtcNow.AddYears(1).ToUnixTimeSeconds().ToString();
        Assert.False(tickets.TryRead(string.Join('.', parts), out _));
    }

    [Fact]
    public void Another_process_cannot_issue_tickets_this_one_accepts()
    {
        var token = new LivePlayTickets().Issue("alice", 1, Later);
        Assert.False(new LivePlayTickets().TryRead(token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dots-at-all")]
    [InlineData("a.b.c.d")]
    [InlineData("!!!.1.2.@@@")]
    public void Anything_else_is_not_a_ticket(string? token)
    {
        Assert.False(new LivePlayTickets().TryRead(token, out _));
    }
}

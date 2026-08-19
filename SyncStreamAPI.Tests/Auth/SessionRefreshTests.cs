using System;
using SyncStreamAPI.Auth;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     Keeping a session alive while it is used.
/// </summary>
/// <remarks>
///     People who used the site every day were still signed out regularly. The cause was the derived token,
///     which mixed in <c>navigator.appVersion</c>, so every browser update produced a token that matched nothing
///     stored and ended the session no matter how recently they had visited. With a random stored token that
///     cannot happen, but expiry was still fixed at the moment of issue, so the thirtieth day signed them out
///     anyway.
/// </remarks>
public class SessionRefreshTests
{
    private static readonly DateTime Issued = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_session_just_issued_is_touched_on_its_first_use()
    {
        // Nothing has recorded a use yet, so the first request is the one that establishes it.
        var (_, session) = RememberTokens.Issue(Issued);

        Assert.True(SessionRefresh.IsDue(session, Issued));
    }

    [Fact]
    public void A_session_touched_a_moment_ago_is_left_alone()
    {
        // Authentication runs on every request, including every hub message, so this decides how often the
        // database is written to.
        var (_, session) = RememberTokens.Issue(Issued);
        SessionRefresh.Apply(session, Issued);

        Assert.False(SessionRefresh.IsDue(session, Issued.AddMinutes(1)));
    }

    [Fact]
    public void A_session_untouched_for_the_interval_is_due_again()
    {
        var (_, session) = RememberTokens.Issue(Issued);
        SessionRefresh.Apply(session, Issued);

        Assert.True(SessionRefresh.IsDue(session, Issued.Add(SessionRefresh.Interval)));
    }

    [Fact]
    public void Using_a_session_pushes_its_deadline_out()
    {
        var (_, session) = RememberTokens.Issue(Issued);
        var originalDeadline = session.ExpiresUtc;

        var later = Issued.AddDays(20);
        SessionRefresh.Apply(session, later);

        Assert.True(session.ExpiresUtc > originalDeadline);
        Assert.Equal(later.Add(RememberTokens.Lifetime), session.ExpiresUtc);
    }

    [Fact]
    public void Somebody_who_visits_daily_is_never_signed_out()
    {
        // The case that was reported: used every day, signed out anyway. Walked a year here, refreshing once a
        // day, which is what an ordinary visit does.
        var (_, session) = RememberTokens.Issue(Issued);

        var day = Issued;
        for (var visits = 0; visits < 365; visits++)
        {
            Assert.True(session.IsUsable(day), $"signed out on day {visits}");
            SessionRefresh.Apply(session, day);
            day = day.AddDays(1);
        }

        Assert.True(session.IsUsable(day));
    }

    [Fact]
    public void Somebody_who_stops_visiting_is_signed_out_eventually()
    {
        // The other half of it. Sliding expiry must still expire, or the window means nothing.
        var (_, session) = RememberTokens.Issue(Issued);
        SessionRefresh.Apply(session, Issued);

        Assert.True(session.IsUsable(Issued.AddDays(29)));
        Assert.False(session.IsUsable(Issued.Add(RememberTokens.Lifetime).AddSeconds(1)));
    }

    [Fact]
    public void Refreshing_does_not_revive_a_revoked_session()
    {
        // Signing out elsewhere, or changing the password, has to win over any amount of continued use.
        var (_, session) = RememberTokens.Issue(Issued);
        session.RevokedUtc = Issued.AddMinutes(5);

        SessionRefresh.Apply(session, Issued.AddMinutes(10));

        Assert.False(session.IsUsable(Issued.AddMinutes(11)));
    }
}

using System;
using SyncStreamAPI.Auth;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     Issuing and recognising a session.
/// </summary>
/// <remarks>
///     Two problems, in order. The tokens were derived from the account and a device string, so the same account
///     on the same device always produced the same token: it never rotated, a password change did not invalidate
///     it, and logging out could not revoke it because the next login produced the identical string. Then they
///     were stored as issued, so reading the table was a thirty-day session takeover on every account.
/// </remarks>
public class RememberTokenTests
{
    private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Two_sessions_never_share_a_token()
    {
        var first = RememberTokens.Issue(Now);
        var second = RememberTokens.Issue(Now);

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.Record.TokenHash, second.Record.TokenHash);
    }

    [Fact]
    public void The_stored_record_does_not_contain_the_token()
    {
        var (token, record) = RememberTokens.Issue(Now);

        Assert.DoesNotContain(token, record.TokenHash);
    }

    [Fact]
    public void A_token_is_recognised_by_its_digest()
    {
        var (token, record) = RememberTokens.Issue(Now);

        Assert.Equal(record.TokenHash, RememberTokens.Digest(token));
    }

    [Fact]
    public void A_different_token_does_not_produce_the_same_digest()
    {
        var (_, record) = RememberTokens.Issue(Now);

        Assert.NotEqual(record.TokenHash, RememberTokens.Digest("some other token"));
    }

    [Fact]
    public void A_session_records_when_it_stops_being_accepted()
    {
        // On the row rather than derived from a constant in code, so shortening the lifetime later does not
        // retroactively end sessions issued under the previous promise.
        var (_, record) = RememberTokens.Issue(Now);

        Assert.Equal(Now, record.Created);
        Assert.Equal(Now.Add(RememberTokens.Lifetime), record.ExpiresUtc);
    }

    [Fact]
    public void A_fresh_session_is_usable()
    {
        var (_, record) = RememberTokens.Issue(Now);

        Assert.True(record.IsUsable(Now));
        Assert.True(record.IsUsable(Now.AddDays(29)));
    }

    [Fact]
    public void An_expired_session_is_not_usable()
    {
        var (_, record) = RememberTokens.Issue(Now);

        Assert.False(record.IsUsable(Now.Add(RememberTokens.Lifetime).AddSeconds(1)));
    }

    [Fact]
    public void A_revoked_session_is_not_usable_even_before_it_expires()
    {
        // What signing out and changing a password now do. The deterministic tokens this replaced could not
        // express it: the same string came back on the next login.
        var (_, record) = RememberTokens.Issue(Now);
        record.RevokedUtc = Now.AddMinutes(1);

        Assert.False(record.IsUsable(Now.AddMinutes(2)));
    }

    [Fact]
    public void A_readable_key_is_not_the_same_twice()
    {
        // For the values that have to stay readable because they are displayed: the stream key and the API key.
        Assert.NotEqual(SyncStreamAPI.Helper.RandomKeys.Readable(), SyncStreamAPI.Helper.RandomKeys.Readable());
    }
}

/// <summary>
///     What counts as an acceptable password.
/// </summary>
/// <remarks>
///     Nothing could be checked before: the browser hashed the password, so every one of them arrived as the same
///     128 characters and the only length rule in the code was measuring the digest.
/// </remarks>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("a reasonable password")]
    [InlineData("12345678")]
    [InlineData("  padded  ")]
    public void An_acceptable_password_is_accepted(string password)
    {
        Assert.True(PasswordPolicy.IsAcceptable(password, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("       ")]
    [InlineData("short")]
    public void A_password_that_is_too_short_or_absent_is_refused(string? password)
    {
        Assert.False(PasswordPolicy.IsAcceptable(password, out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void An_absurdly_long_password_is_refused()
    {
        // So a request cannot ask the server to run 210,000 rounds of PBKDF2 over a megabyte of input.
        Assert.False(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MaximumLength + 1), out var reason));
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void The_longest_allowed_password_is_still_allowed()
    {
        Assert.True(PasswordPolicy.IsAcceptable(new string('a', PasswordPolicy.MaximumLength), out _));
    }
}

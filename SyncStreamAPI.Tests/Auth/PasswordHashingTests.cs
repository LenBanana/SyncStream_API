using System;
using System.Security.Cryptography;
using System.Text;
using SyncStreamAPI.Auth;
using Xunit;

namespace SyncStreamAPI.Tests.Auth;

/// <summary>
///     Storing and checking a password.
/// </summary>
/// <remarks>
///     The state this replaces stored the SHA512 digest the browser sent, verbatim, and compared it with string
///     equality. Since the server accepted that digest as the credential, the stored value was the credential:
///     reading the database authenticated you as anybody, with no cracking step at all.
/// </remarks>
public class PasswordHashingTests
{
    /// <summary>The digest the browser used to send, which is what old rows contain.</summary>
    private static string LegacyDigest(string password)
    {
        return Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
    }

    [Fact]
    public void A_stored_hash_does_not_contain_the_password_or_its_digest()
    {
        var stored = PasswordHashing.Hash("correct horse battery");

        Assert.DoesNotContain("correct horse battery", stored);
        Assert.DoesNotContain(LegacyDigest("correct horse battery"), stored);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // Per-password salt, so identical passwords do not reveal each other and one cracked hash is one
        // account rather than every account that shares the password.
        var first = PasswordHashing.Hash("same password");
        var second = PasswordHashing.Hash("same password");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void The_right_password_is_accepted()
    {
        var stored = PasswordHashing.Hash("correct horse battery");

        Assert.Equal(PasswordVerdict.Match, PasswordHashing.Verify(stored, "correct horse battery"));
    }

    [Theory]
    [InlineData("wrong password")]
    [InlineData("correct horse batter")]
    [InlineData("Correct horse battery")]
    [InlineData("")]
    public void A_wrong_password_is_refused(string attempt)
    {
        var stored = PasswordHashing.Hash("correct horse battery");

        Assert.Equal(PasswordVerdict.NoMatch, PasswordHashing.Verify(stored, attempt));
    }

    [Fact]
    public void The_digest_is_not_accepted_in_place_of_the_password()
    {
        // The heart of the old problem. Whoever read the database could send the stored string straight back
        // and be logged in. Nothing derived from the stored value may work as the password.
        var stored = PasswordHashing.Hash("correct horse battery");

        Assert.Equal(PasswordVerdict.NoMatch, PasswordHashing.Verify(stored, stored));
        Assert.Equal(PasswordVerdict.NoMatch,
            PasswordHashing.Verify(stored, LegacyDigest("correct horse battery")));
    }

    [Fact]
    public void The_stored_form_names_its_own_cost()
    {
        // So the cost can be raised, or the construction replaced, without a migration: an account whose hash
        // does not match the current parameters is rewritten the next time its owner signs in.
        var stored = PasswordHashing.Hash("correct horse battery");
        var parts = stored.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal("PBKDF2-SHA512", parts[0]);
        Assert.Equal("210000", parts[1]);
    }

    [Fact]
    public void A_hash_at_a_lower_cost_still_verifies_but_asks_to_be_rewritten()
    {
        var stored = string.Join('$', "PBKDF2-SHA512", "1000",
            Convert.ToBase64String(new byte[16]),
            Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes("correct horse battery"), new byte[16], 1000, HashAlgorithmName.SHA512, 64)));

        Assert.Equal(PasswordVerdict.MatchNeedsUpgrade, PasswordHashing.Verify(stored, "correct horse battery"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("PBKDF2-SHA512$210000$notbase64$notbase64")]
    [InlineData("PBKDF2-SHA512$notanumber$AAAA$AAAA")]
    [InlineData("ROT13$210000$AAAA$AAAA")]
    public void An_unreadable_stored_value_refuses_rather_than_throwing(string? stored)
    {
        Assert.Equal(PasswordVerdict.NoMatch, PasswordHashing.Verify(stored, "correct horse battery"));
    }

    /// <summary>
    ///     The part that has to be invisible: nobody is signed out and nobody is asked to set a new password.
    /// </summary>
    public class MigratingFromTheOldFormat
    {
        [Fact]
        public void A_wrapped_digest_accepts_the_original_password()
        {
            // The database held only the digest, so wrapping it needs no help from the account's owner. The
            // server can still check a typed password against it, because it can compute the same digest.
            var wrapped = PasswordHashing.WrapLegacyDigest(LegacyDigest("correct horse battery"));

            Assert.Equal(PasswordVerdict.MatchNeedsUpgrade,
                PasswordHashing.Verify(wrapped, "correct horse battery"));
        }

        [Fact]
        public void A_wrapped_digest_refuses_the_wrong_password()
        {
            var wrapped = PasswordHashing.WrapLegacyDigest(LegacyDigest("correct horse battery"));

            Assert.Equal(PasswordVerdict.NoMatch, PasswordHashing.Verify(wrapped, "wrong password"));
        }

        [Fact]
        public void A_wrapped_digest_no_longer_contains_the_digest()
        {
            // Which is the point of doing it at startup rather than waiting for each person to sign in: the
            // replayable value stops being in the database for everybody at once.
            var digest = LegacyDigest("correct horse battery");
            var wrapped = PasswordHashing.WrapLegacyDigest(digest);

            Assert.DoesNotContain(digest, wrapped);
        }

        [Fact]
        public void The_digest_is_not_accepted_in_place_of_the_password_after_wrapping()
        {
            var digest = LegacyDigest("correct horse battery");
            var wrapped = PasswordHashing.WrapLegacyDigest(digest);

            Assert.Equal(PasswordVerdict.NoMatch, PasswordHashing.Verify(wrapped, digest));
        }

        [Fact]
        public void An_unwrapped_row_still_accepts_the_password_and_asks_to_be_rewritten()
        {
            // A row the startup pass has not reached, or one restored from an older backup, must not lock its
            // owner out.
            var legacy = LegacyDigest("correct horse battery");

            Assert.Equal(PasswordVerdict.MatchNeedsUpgrade,
                PasswordHashing.Verify(legacy, "correct horse battery"));
            Assert.Equal(PasswordVerdict.NoMatch, PasswordHashing.Verify(legacy, "wrong password"));
        }

        [Fact]
        public void An_uppercase_digest_is_read_the_same_as_a_lowercase_one()
        {
            // The browser produced lowercase, but a value that has been through other tooling may not be.
            var legacy = LegacyDigest("correct horse battery").ToUpperInvariant();

            Assert.Equal(PasswordVerdict.MatchNeedsUpgrade,
                PasswordHashing.Verify(legacy, "correct horse battery"));
        }

        [Theory]
        [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" +
                    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
        [InlineData("short", false)]
        [InlineData("PBKDF2-SHA512$210000$AAAA$AAAA", false)]
        [InlineData(null, false)]
        public void A_legacy_row_is_told_apart_from_a_hash(string? stored, bool expected)
        {
            Assert.Equal(expected, PasswordHashing.IsLegacyDigest(stored));
        }

        [Fact]
        public void A_digest_length_string_that_is_not_hex_is_not_mistaken_for_one()
        {
            var notHex = new string('z', 128);

            Assert.False(PasswordHashing.IsLegacyDigest(notHex));
        }
    }
}

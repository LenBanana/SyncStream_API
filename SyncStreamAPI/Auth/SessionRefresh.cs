using System;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Keeps a session alive while it is being used.
/// </summary>
/// <remarks>
///     Expiry was fixed at the moment of issue, so somebody who visited every day was still signed out on the
///     thirtieth day for no reason they could see. What should end a session is not using it, not having had it
///     for a while.
///     <para>
///         This is not what caused the frequent sign-outs before any of this work. The token was derived as
///         <c>Sha256(id + username + navigator.appVersion + salt)</c> and restoring a session re-derived it,
///         answering only if that exact string was already stored. <c>navigator.appVersion</c> carries the full
///         browser version, so every browser update produced a different token, matched nothing, and ended the
///         session. Daily use could not help, because the trigger was an update rather than inactivity. A random
///         stored token cannot behave that way, and this makes the remaining fixed deadline slide too.
///     </para>
/// </remarks>
public static class SessionRefresh
{
    /// <summary>
    ///     How stale the last-used stamp may get before the session is touched again.
    /// </summary>
    /// <remarks>
    ///     Authentication happens on every request, including every hub message, so refreshing on each one would
    ///     mean a database write per request and a fresh cookie on every response. An hour is far finer than the
    ///     thirty-day window needs.
    /// </remarks>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>Whether this session has gone long enough without being touched to be worth touching.</summary>
    public static bool IsDue(DbRememberToken session, DateTime nowUtc)
    {
        return session.LastUsedUtc == null || nowUtc - session.LastUsedUtc.Value >= Interval;
    }

    /// <summary>
    ///     Records the use and pushes the deadline out to a full lifetime from now.
    /// </summary>
    /// <remarks>
    ///     There is deliberately no cap on total age. A session ends when it goes unused for the whole window,
    ///     when its owner signs out, or when the password changes. Adding an absolute maximum would mean a
    ///     regular sign-out that no behaviour of the user's could avoid, which is the thing being removed.
    /// </remarks>
    public static void Apply(DbRememberToken session, DateTime nowUtc)
    {
        session.LastUsedUtc = nowUtc;
        session.ExpiresUtc = nowUtc.Add(RememberTokens.Lifetime);
    }
}

using System.Linq;
using Microsoft.Extensions.Logging;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.DataContext;

/// <summary>
///     Rewrites stored secrets that are still in a form nobody should be storing.
/// </summary>
/// <remarks>
///     Accounts and rooms held the SHA512 digest the browser produced, and the server accepted that same digest
///     as the credential, so the stored value was the credential: reading the table authenticated you with no
///     cracking step at all. Fixing that per account as its owner next signs in would leave the rows of everyone
///     who has drifted away in that state indefinitely, and they are the ones least likely to notice.
///     <para>
///         The digest is enough to wrap on its own. Running it through the password hash produces something the
///         server can still check a typed password against, because it can compute the same digest, so the change
///         needs nothing from the account's owner and is invisible from outside. The wrapped form is then
///         replaced with a hash of the real password on the first sign-in, which is the first moment the
///         plaintext exists here.
///     </para>
/// </remarks>
public static class StoredSecretUpgrade
{
    public static void Run(PostgresContext context, ILogger logger)
    {
        UpgradeAccountPasswords(context, logger);
        UpgradeRoomPasswords(context, logger);
    }

    private static void UpgradeAccountPasswords(PostgresContext context, ILogger logger)
    {
        // Length and shape are the test, so a row already hashed is left alone and the pass is safe to repeat.
        var candidates = context.Users
            .Where(user => user.password != null && user.password.Length == 128)
            .ToList()
            .Where(user => PasswordHashing.IsLegacyDigest(user.password))
            .ToList();

        if (candidates.Count == 0) return;

        foreach (var user in candidates) user.password = PasswordHashing.WrapLegacyDigest(user.password);

        context.SaveChanges();
        logger.LogWarning(
            "Wrapped {Count} account password(s) that were stored as a bare digest. Each will be replaced with " +
            "a hash of the real password the next time its owner signs in", candidates.Count);
    }

    private static void UpgradeRoomPasswords(PostgresContext context, ILogger logger)
    {
        var candidates = context.Rooms
            .Where(room => room.password != null && room.password.Length == 128)
            .ToList()
            .Where(room => PasswordHashing.IsLegacyDigest(room.password))
            .ToList();

        if (candidates.Count == 0) return;

        foreach (var room in candidates) room.password = PasswordHashing.WrapLegacyDigest(room.password);

        context.SaveChanges();
        logger.LogWarning(
            "Wrapped {Count} room password(s) that were stored as a bare digest", candidates.Count);
    }
}

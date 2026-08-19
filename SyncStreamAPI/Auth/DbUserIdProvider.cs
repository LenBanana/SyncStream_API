using Microsoft.AspNetCore.SignalR;

namespace SyncStreamAPI.Auth;

/// <summary>
///     Maps a connection to its account id so <c>Clients.User(id)</c> reaches every connection that account
///     has open.
/// </summary>
/// <remarks>
///     Without this registered, <c>Clients.User(...)</c> matches on the default name claim and silently
///     reaches nobody when given anything else. <c>BanUser</c> passed a connection id to it, so the ban
///     notice never arrived.
/// </remarks>
public class DbUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirst(SyncStreamClaims.UserId)?.Value;
    }
}

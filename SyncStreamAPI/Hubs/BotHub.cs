using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Helper;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    /// <summary>
    ///     Opts an already-authenticated connection into the bot broadcast channel.
    /// </summary>
    /// <remarks>
    ///     The API key used to arrive as an argument and be looked up here. A programmatic client is not a
    ///     browser, so it can send the key as a request header on the negotiate and be authenticated by the
    ///     API-key scheme like any other request. What remains is the opt-in itself, since holding a key does
    ///     not by itself mean a connection wants bot broadcasts.
    /// </remarks>
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task AuthenticateBot()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, General.BottedInGroupName);
        await Clients.Caller.sendBotConfirmAuthenticate(true);
    }
}

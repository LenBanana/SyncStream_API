using System.Linq;
using System.Threading.Tasks;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.PostgresModels;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task GetLiveUsers()
    {
        var liveUser = _manager.LiveUsers;
        if (liveUser.Count > 0) await Clients.Caller.getliveusers(liveUser.Select(x => x.ToDTO()).ToList());
    }

    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task GetUsersWatching(string name)
    {
        var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.userName == name);
        if (liveUser != null) await Clients.Caller.getwatchingusers(liveUser.watchMember);
    }
}
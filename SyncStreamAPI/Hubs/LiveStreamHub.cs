using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.PostgresModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task GetLiveUsers(string token)
        {
            var liveUser = _manager.LiveUsers;
            if (liveUser.Count > 0)
            {
                await Clients.Caller.getliveusers(liveUser.Select(x => x.ToDTO()).ToList());
            }
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task GetUsersWatching(string token, string name)
        {
            var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.userName == name);
            if (liveUser != null)
            {
                await Clients.Caller.getwatchingusers(liveUser.watchMember);
            }
        }
    }
}

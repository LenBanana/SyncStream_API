using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.PostgresModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task GetLiveUsers(string token)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Approved)
                {
                    var liveUser = _manager.LiveUsers;
                    if (liveUser.Count > 0)
                        await Clients.Caller.getliveusers(liveUser);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'GetLiveUsers'");
                Console.WriteLine(ex.Message);
            }
        }

        public async Task GetUsersWatching(string token, string name)
        {
            try
            {
                var dbUser = _postgres.Users?.Include(x => x.RememberTokens).Where(x => x.RememberTokens != null && x.RememberTokens.Any(y => y.Token == token)).FirstOrDefault();
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Approved)
                {
                    var liveUser = _manager.LiveUsers.FirstOrDefault(x => x.userName == name);
                    if (liveUser != null)
                        await Clients.Caller.getwatchingusers(liveUser.watchMember);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in 'GetUsersWatching'");
                Console.WriteLine(ex.Message);
            }
        }
    }
}

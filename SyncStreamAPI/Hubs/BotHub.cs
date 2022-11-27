using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task AuthenticateBot(string apiKey)
        {
            var dbUser = _postgres.Users?.FirstOrDefault(x => x.ApiKey == apiKey);
            if (dbUser != null)
                await Groups.AddToGroupAsync(Context.ConnectionId, General.BottedInGroupName);
            else
                await Clients.Caller.dialog(new Dialog() { Header = "Api Error", Question = "API Key hasn't been found", Answer1 = "Ok" });
            await Clients.Caller.sendBotConfirmAuthenticate(dbUser != null);
        }
    }
}

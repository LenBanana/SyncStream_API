using SyncStreamAPI.Models.Bots;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        public Task sendBotChannelUpdate(BotLiveChannelInfo info);
        public Task sendBotConfirmAuthenticate(bool confirm);
    }
}

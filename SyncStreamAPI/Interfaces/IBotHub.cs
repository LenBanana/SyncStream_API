using System.Threading.Tasks;
using SyncStreamAPI.Models.Bots;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    public Task sendBotChannelUpdate(BotLiveChannelInfo info);
    public Task sendBotConfirmAuthenticate(bool confirm);
}
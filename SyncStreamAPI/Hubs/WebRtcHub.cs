using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task CreateStreamOffer(string token, WebRtcClientOffer offer)
        {
            await Clients.Others.sendClientOffer(offer);
        }
        
        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task CreateStreamAnswer(string token, WebRtcClientOffer answer)
        {
            await Clients.Others.sendClientAnswer(answer);
        }
        
        [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
        public async Task SendIceCandidate(string token, WebRtcIceCandidate iceCandidate)
        {
            await Clients.Others.sendIceCandidate(iceCandidate);
        }
    }
}

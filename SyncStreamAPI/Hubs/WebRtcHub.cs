using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models.WebRTC;
using SyncStreamAPI.PostgresModels;
using System.Threading.Tasks;
using Org.WebRtc;
using SyncStreamAPI.Helper;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task JoinWebRtcStream(string token, string roomId)
        {
            var room = MainManager.GetRoom(roomId);
            if (room == null || room.CurrentStreamer?.Length == 0) return;
            if (room.CurrentStreamer == Context.ConnectionId) return;
            await Clients.Client(room.CurrentStreamer).joinWebRtcStream(Context.ConnectionId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task StartWebRtcStream(string token, string roomId)
        {
            var room = MainManager.GetRoom(roomId);
            if (room == null) return;
            if (room.CurrentStreamer != null)
            {
                await StopWebRtcStream(token, roomId);
            }

            room.CurrentStreamer = Context.ConnectionId;
            await Clients.GroupExcept(room.uniqueId, new[] { Context.ConnectionId })
                .startWebRtcStream(Context.ConnectionId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendOfferToViewer(string token, string connectionId, WebRtcClientOffer offer)
        {
            await Clients.Client(connectionId).sendOfferToViewer(offer);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task StopWebRtcStream(string token, string roomId)
        {
            var room = MainManager.GetRoom(roomId);
            if (room == null || room.CurrentStreamer?.Length == 0) return;
            if (room.CurrentStreamer != Context.ConnectionId) return;
            await Clients.GroupExcept(room.uniqueId, new[] { Context.ConnectionId })
                .stopWebRtcStream(room.CurrentStreamer);
            room.CurrentStreamer = null;
            var type = await SendPlayerType(room);
            await Clients.Group(room.uniqueId).stopWebRtcStream(Context.ConnectionId);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task CreateStreamAnswer(string token, string roomId, WebRtcClientOffer answer)
        {
            var room = MainManager.GetRoom(roomId);
            if (room == null || room.CurrentStreamer?.Length == 0) return;
            if (room.CurrentStreamer == Context.ConnectionId) return;
            answer.ViewerId = Context.ConnectionId;
            await Clients.Client(room.CurrentStreamer).sendClientAnswer(answer);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendIceCandidate(string token, string roomId, object iceCandidate)
        {
            var room = MainManager.GetRoom(roomId);
            if (room == null || room.CurrentStreamer?.Length == 0) return;
            if (room.CurrentStreamer == Context.ConnectionId) return;
            await Clients.Client(room.CurrentStreamer).sendIceCandidate(iceCandidate);
        }

        [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
        public async Task SendIceCandidateToViewer(string token, string connectionId, object iceCandidate)
        {
            await Clients.Client(connectionId).sendIceCandidate(iceCandidate);
        }
    }
}
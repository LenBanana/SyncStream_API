using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task dialog(Dialog dialog);

        Task sendserver(Server server);

        Task videoupdate(DreckVideo video);

        Task playlistupdate(List<DreckVideo> videos);

        Task getrooms(List<Room> rooms);

        Task isplayingupdate(bool isplaying);

        Task twitchTimeUpdate(double time);

        Task timeupdate(double time);

        Task twitchPlaying(bool playing);

        Task adduserupdate(int errorCode);

        Task userupdate(List<Member> members);

        Task hostupdate(bool isHost);

        Task sendmessage(List<ChatMessage> chatMessages);

        Task PingTest(DateTime dateSend);

        Task PrivateMessage(string message);
    }
}

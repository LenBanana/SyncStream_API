using SyncStreamAPI.MariaModels;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public interface IServerHub
    {
        Task userlogin(User user);

        Task dialog(Dialog dialog);

        Task userRegister(User user);

        Task rememberToken(RememberToken token);

        Task getusers(List<User> users);

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

        Task whiteboardjoin(List<Drawing> drawings);

        Task whiteboardupdate(List<Drawing> newDrawings);

        Task whiteboardclear(bool clear);

        Task whiteboardundo(string UUID);

        Task whiteboardredo(string UUID);

        Task PingTest(DateTime dateSend);

        Task PrivateMessage(string message);
    }
}

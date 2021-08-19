using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task SendMessage(ChatMessage message, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            message.time = DateTime.Now;
            MainServer.chatmessages.Add(message);
            if (MainServer.chatmessages.Count >= 100)
                MainServer.chatmessages.RemoveAt(0);
            Member sender = MainServer.members.FirstOrDefault(x => Context.ConnectionId == x.ConnectionId);
            if (!room.PlayingGallows)
            {
                await Clients.Group(UniqueId).sendmessage(message);
            }
            else
            {
                if ((sender != null && sender.ishost) || sender.guessedGallow)
                    return;
                if (message.message.ToLower() == room.GallowWord.ToLower())
                {
                    var guessedGallow = MainServer.members.Where(x => x.guessedGallow);
                    var points = 10 - guessedGallow.Count();
                    sender.gallowPoints += points > 0 ? points : 0;
                    sender.guessedGallow = true;
                    ChatMessage correntAnswerServerMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} answered correctly" };
                    ChatMessage correntAnswerPrivateMsg = new ChatMessage() { time = DateTime.Now, username = "System", message = $"{message.username} you answered correct. You've been awarded {points} points" };
                    await Clients.Caller.sendmessage(correntAnswerPrivateMsg);
                    await Clients.Group(UniqueId).sendmessage(correntAnswerServerMsg);
                    if (MainServer.members.Where(x => !x.ishost).All(x => x.guessedGallow))
                    {
                        int idx = MainServer.members.FindIndex(x => x.ishost);
                        idx = (idx + 1) == MainServer.members.Count ? 0 : idx + 1;
                        MainServer.members.ForEach(x => x.guessedGallow = false);
                        room.UpdateGallowWord();
                        await Clients.Group(UniqueId).playinggallows(room.GallowWord);
                        await ChangeHost(MainServer.members[idx].username, UniqueId);
                    }
                    await Clients.Group(UniqueId).gallowusers(MainServer.members.Select(x => x.ToDTO()).ToList());
                }
                else
                {
                    await Clients.Group(UniqueId).sendmessage(message);
                }
            }
        }

        public async Task GetMessages(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            await Clients.Caller.sendmessages(MainServer.chatmessages);
        }

        public async Task SendPrivateMessage(string UniqueId, string FromUser, string ToUser, string Message)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                Server MainServer = room.server;
                Member FromIdx = MainServer.members.FirstOrDefault(x => x.username == FromUser);
                Member ToIdx = MainServer.members.FirstOrDefault(x => x.username == ToUser);
                if (FromIdx != null && ToIdx != null)
                {
                    string FullMessage = FromIdx.AddMessage(ToUser, Message);
                    await Clients.Caller.PrivateMessage(FullMessage);
                    await Clients.Client(ToIdx.ConnectionId).PrivateMessage(FullMessage);
                }
                else
                {
                    throw new Exception("User was not found");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Clients.Caller.dialog(new Dialog() { Header = "Message Error", Question = "There has been an error trying to send your message, please try again.", Answer1 = "Ok" });
            }
        }
        public async Task ClearChat(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                Server MainServer = room.server;
                MainServer.chatmessages = new List<ChatMessage>();
                await Clients.Group(UniqueId).sendmessages(MainServer.chatmessages);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Clients.Caller.dialog(new Dialog() { Header = "Message Error", Question = "There has been an error trying to send your message, please try again.", Answer1 = "Ok" });
            }
        }
    }
}

using SyncStreamAPI.Enums;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
            var lowerMessage = message.message.ToLower().Trim();
            if (lowerMessage.StartsWith("/"))
            {
                if (lowerMessage.StartsWith("/w"))
                {
                    var regEx = new Regex("^\\/[wW] (?<wName>[^\\s]+) (?<wMsg>.*)$");
                    var match = regEx.Match(message.message);
                    if (match.Success)
                    {
                        var wName = match.Groups["wName"].Value.Trim();
                        var wMsg = match.Groups["wMsg"].Value.Trim();
                        var receiverMember = MainServer.members.FirstOrDefault(x => x.username.ToLower() == wName.ToLower());
                        if (receiverMember != null)
                        {
                            var wChatUserMsg = new WhisperUserMessage() { username = $"To {wName}", message = $"{wMsg}" };
                            var wChatReceiverMsg = new WhisperReceiverMessage() { username = $"From {message.username}", message = $"{wMsg}" };
                            await Clients.Caller.sendmessage(wChatUserMsg);
                            if (wName.ToLower() == "all")
                                await Clients.Others.sendmessage(wChatReceiverMsg);
                            else
                                await Clients.Client(receiverMember.ConnectionId).sendmessage(wChatReceiverMsg);
                            return;
                        }
                        else
                        {
                            var errorMsg = new SystemMessage($"Could not find user: {wName}");
                            await Clients.Caller.sendmessage(errorMsg);
                            return;
                        }
                    }
                }
                else if (lowerMessage.StartsWith("/cp"))
                {
                    if (MainServer.playlist.Count > 1)
                    {
                        var pl = new List<DreckVideo>();
                        pl.Add(MainServer.playlist[0]);
                        MainServer.playlist = pl;
                        await Clients.Group(UniqueId).playlistupdate(MainServer.playlist);
                    }
                }
                else if (lowerMessage.StartsWith("/c") && !lowerMessage.StartsWith("/chess"))
                {
                    await ClearChat(UniqueId);
                }
                else if (lowerMessage.StartsWith("/playgallows") || lowerMessage.StartsWith("/playgallow") || lowerMessage.StartsWith("/gallows") || lowerMessage.StartsWith("/gallow") || lowerMessage.StartsWith("/galgenraten") || lowerMessage.StartsWith("/galgen") || lowerMessage.StartsWith("/g"))
                {
                    var split = lowerMessage.Split(" ");
                    var lang = Language.German;
                    var length = 90;
                    if (split.Length > 1)
                        lang = split[1].StartsWith("e") ? Language.English : Language.German;
                    if (split.Length > 2)
                        int.TryParse(split[2], out length);
                    await PlayGallowsSettings(UniqueId, lang, length);
                }
                else if (lowerMessage.StartsWith("/s") || lowerMessage.StartsWith("/spectate"))
                {
                    await SpectateBlackjack(UniqueId);
                }
                else if (lowerMessage.StartsWith("/ai"))
                {
                    await AddBlackjackAi(UniqueId);
                }
                else if (lowerMessage.StartsWith("/mai"))
                {
                    await MakeAi(UniqueId);
                }
                else if (lowerMessage.StartsWith("/playblackjack") || lowerMessage.StartsWith("/playbj") || lowerMessage.StartsWith("/blackjack") || lowerMessage.StartsWith("/bj") || lowerMessage.StartsWith("/b"))
                {
                    await PlayBlackjack(UniqueId);
                }
                else if (lowerMessage.StartsWith("/chess"))
                {
                    lowerMessage += " ";
                    var regEx = new Regex("^\\/chess (?<wName1>[^\\s]+) (?<wName2>[^\\s]+)?$");
                    var match = regEx.Match(message.message);
                    if (match.Success || lowerMessage.Trim() == "/chess")
                    {
                        var user1 = match.Groups["wName1"].Value.Trim();
                        var user2 = match.Groups["wName2"].Value.Trim();
                        var lightPlayerAi = user1.ToLower() == "ai";
                        var darkPlayerAi = user2.ToLower() == "ai";
                        var member1 = MainServer.members.FirstOrDefault(x => x.username == user1);
                        var member2 = MainServer.members.FirstOrDefault(x => x.username == user1);
                        if (room.GameMode != GameMode.Chess)
                            await PlayChess(UniqueId, member1 != null ? member1.username : "", member2 != null ? member2.username : "", lightPlayerAi, darkPlayerAi);
                        else
                            await EndChess(UniqueId);
                    }
                }
                return;
            }
            MainServer.chatmessages.Add(message);
            if (MainServer.chatmessages.Count >= 100)
                MainServer.chatmessages.RemoveAt(0);
            switch (room.GameMode)
            {
                case GameMode.NotPlaying:
                case GameMode.Blackjack:
                case GameMode.Chess:
                    await Clients.Group(MainServer.RoomId).sendmessage(message);
                    break;
                case GameMode.Gallows:
                    Member sender = MainServer.members.FirstOrDefault(x => Context.ConnectionId == x.ConnectionId);
                    var game = room.GallowGame;
                    await _gallowGameManager.PlayGallow(game, sender, message, game.GallowTime);
                    break;
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
                Console.WriteLine(ex.ToString());
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
                Console.WriteLine(ex.ToString());
                await Clients.Caller.dialog(new Dialog() { Header = "Message Error", Question = "There has been an error trying to send your message, please try again.", Answer1 = "Ok" });
            }
        }
    }
}

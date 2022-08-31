﻿using SyncStreamAPI.Enums.Games;
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
                            await Clients.Client(receiverMember.ConnectionId).sendmessage(wChatReceiverMsg);
                            return;
                        }
                        else
                        {
                            var errorMsg = new SystemMessage() { message = $"Could not find user: {wName}" };
                            await Clients.Caller.sendmessage(errorMsg);
                            return;
                        }
                    }
                }
                else if (lowerMessage.StartsWith("/c"))
                {
                    await ClearChat(UniqueId);
                }
                else if (lowerMessage.StartsWith("/playgallows") || lowerMessage.StartsWith("/playgallow") || lowerMessage.StartsWith("/gallows") || lowerMessage.StartsWith("/gallow") || lowerMessage.StartsWith("/galgenraten") || lowerMessage.StartsWith("/galgen") || lowerMessage.StartsWith("/g"))
                {
                    var split = lowerMessage.Split(" ");
                    var lang = Enums.Language.German;
                    var length = 90;
                    if (split.Length > 1)
                        lang = split[1].StartsWith("e") ? Enums.Language.English : Enums.Language.German;
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
                return;
            }
            MainServer.chatmessages.Add(message);
            if (MainServer.chatmessages.Count >= 100)
                MainServer.chatmessages.RemoveAt(0);
            switch (room.GameMode)
            {
                case GameMode.NotPlaying:
                case GameMode.Blackjack:
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

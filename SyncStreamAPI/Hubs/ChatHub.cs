using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Enums.Games;
using SyncStreamAPI.Models;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    public async Task SendMessage(ChatMessage message, string uniqueId)
    {
        var room = GetRoom(uniqueId);
        if (room == null) return;

        var mainServer = room.server;
        var lowerMessage = message.message.ToLower().Trim();

        if (!lowerMessage.StartsWith("/"))
        {
            mainServer.chatmessages.Add(message);
            if (mainServer.chatmessages.Count >= 100) mainServer.chatmessages.RemoveAt(0);

            switch (room.GameMode)
            {
                case GameMode.NotPlaying:
                case GameMode.Blackjack:
                case GameMode.Chess:
                    await Clients.Group(mainServer.RoomId).sendmessage(message);
                    break;
                case GameMode.Gallows:
                    var sender = mainServer.members.FirstOrDefault(x => Context.ConnectionId == x.ConnectionId);
                    await _gallowGameManager.PlayGallow(room.GallowGame, sender, message, room.GallowGame.GallowTime);
                    break;
            }

            return;
        }

        var args = lowerMessage.Split(" ").Skip(1).ToArray();
        var command = lowerMessage.Split(" ", 2)[0];
        Match match;

        async Task WhisperHandler()
        {
            match = new Regex("^\\/[wW] (?<wName>[^\\s]+) (?<wMsg>.*)$").Match(message.message);
            if (!match.Success) return;

            var wName = match.Groups["wName"].Value.Trim();
            var wMsg = match.Groups["wMsg"].Value.Trim();
            var receiverMember =
                mainServer.members.FirstOrDefault(x => x.username.Equals(wName, StringComparison.OrdinalIgnoreCase));

            if (receiverMember != null)
            {
                var wChatUserMsg = new WhisperUserMessage { username = $"To {wName}", message = $"{wMsg}" };
                var wChatReceiverMsg = new WhisperReceiverMessage
                    { username = $"From {message.username}", message = $"{wMsg}" };
                await Task.WhenAll(
                    Clients.Caller.sendmessage(wChatUserMsg),
                    wName.Equals("all", StringComparison.OrdinalIgnoreCase)
                        ? Clients.Others.sendmessage(wChatReceiverMsg)
                        : Clients.Client(receiverMember.ConnectionId).sendmessage(wChatReceiverMsg));
            }
            else
            {
                await Clients.Caller.sendmessage(new SystemMessage($"Could not find user: {wName}"));
            }
        }

        async Task ChessHandler()
        {
            match = new Regex("^\\/chess (?<wName1>[^\\s]+) (?<wName2>[^\\s]+)?$").Match(message.message);
            var user1 = match.Groups["wName1"].Value.Trim();
            var user2 = match.Groups["wName2"].Value.Trim();

            var lightPlayerAi = user1.Equals("ai", StringComparison.OrdinalIgnoreCase);
            var darkPlayerAi = user2.Equals("ai", StringComparison.OrdinalIgnoreCase);

            var member1 =
                mainServer.members.FirstOrDefault(x => x.username.Equals(user1, StringComparison.OrdinalIgnoreCase));
            var member2 =
                mainServer.members.FirstOrDefault(x => x.username.Equals(user2, StringComparison.OrdinalIgnoreCase));

            if (room.GameMode != GameMode.Chess)
                await PlayChess(uniqueId, member1?.username, member2?.username, lightPlayerAi, darkPlayerAi);
            else
                await EndChess(uniqueId);
        }

        var commandHandlers = new Dictionary<string, Func<Task>>
        {
            { "/w", WhisperHandler },
            {
                "/cp", async () =>
                {
                    if (mainServer.playlist.Count > 1)
                    {
                        mainServer.playlist = new List<DreckVideo> { mainServer.playlist[0] };
                        await Clients.Group(uniqueId).playlistupdate(mainServer.playlist);
                    }
                }
            },
            { "/c", async () => await ClearChat(uniqueId) },
            {
                "/playgallows", async () =>
                {
                    var lang = args.Length > 1 && args[1].StartsWith("e") ? Language.English : Language.German;
                    int.TryParse(args.ElementAtOrDefault(2), out var len);
                    await PlayGallowsSettings(uniqueId, lang, len == 0 ? 90 : len);
                }
            },
            { "/s", async () => await SpectateBlackjack(uniqueId) },
            { "/ai", async () => await AddBlackjackAi(uniqueId) },
            { "/mai", async () => await MakeAi(uniqueId) },
            { "/playblackjack", async () => await PlayBlackjack(uniqueId) },
            { "/chess", ChessHandler }
        };

        if (commandHandlers.TryGetValue(command, out var commandHandler)) await commandHandler();
    }

    public async Task GetMessages(string UniqueId)
    {
        var room = GetRoom(UniqueId);
        if (room == null) return;

        var MainServer = room.server;
        await Clients.Caller.sendmessages(MainServer.chatmessages);
    }

    public async Task SendPrivateMessage(string uniqueId, string fromUser, string toUser, string message)
    {
        try
        {
            var room = GetRoom(uniqueId)?.server;
            if (room == null) return;

            var fromMember = room.members.FirstOrDefault(x => x.username == fromUser);
            var toMember = room.members.FirstOrDefault(x => x.username == toUser);
            if (fromMember == null || toMember == null) throw new Exception("User was not found");

            var fullMessage = fromMember.AddMessage(toUser, message);
            await Task.WhenAll(Clients.Caller.PrivateMessage(fullMessage),
                Clients.Client(toMember.ConnectionId).PrivateMessage(fullMessage));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            await Clients.Caller.dialog(new Dialog
            {
                Header = "Message Error",
                Question = "There has been an error trying to send your message, please try again.", Answer1 = "Ok"
            });
        }
    }

    public async Task ClearChat(string UniqueId)
    {
        try
        {
            var room = GetRoom(UniqueId);
            if (room == null) return;

            var MainServer = room.server;
            MainServer.chatmessages = new List<ChatMessage>();
            await Clients.Group(UniqueId).sendmessages(MainServer.chatmessages);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            await Clients.Caller.dialog(new Dialog
            {
                Header = "Message Error",
                Question = "There has been an error trying to send your message, please try again.", Answer1 = "Ok"
            });
        }
    }
}
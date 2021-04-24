using HtmlAgilityPack;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using SyncStreamAPI.Controllers;
using SyncStreamAPI.DataContext;
using SyncStreamAPI.Helper;
using SyncStreamAPI.MariaModels;
using SyncStreamAPI.Models;
using SyncStreamAPI.ServerData;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace SyncStreamAPI.Hubs
{
    [EnableCors("MyPolicy")]
    public class ServerHub : Hub
    {
        DateTime lastPlayPause = DateTime.Now;
        IConfiguration Configuration { get; }

        DataManager _manager;

        MariaContext _maria;

        public ServerHub(IConfiguration configuration, DataManager manager, MariaContext maria)
        {
            Configuration = configuration;
            _manager = manager;
            _maria = maria;
        }

        public async Task LoginRequest(User requestUser)
        {
            User user = _maria.Users.FirstOrDefault(x => x.username == requestUser.username && x.password == requestUser.password);
            if (user != null)
                user.password = "";
            await Clients.Caller.SendAsync("userlogin", user);
        }

        public async Task RegisterRequest(User requestUser)
        {
            User user = null;
            if (!_maria.Users.Any(x => x.username == requestUser.username))
            {
                if (requestUser.username.Length < 2 || requestUser.username.Length > 20)
                {
                    await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok" });
                    return;
                }
                await _maria.Users.AddAsync(requestUser);
                await _maria.SaveChangesAsync();
                user = requestUser;
                user.password = "";
            }
            await Clients.Caller.SendAsync("userRegister", user);
        }

        public async Task GenerateRememberToken(User requestUser, string userInfo)
        {
            if (_maria.Users.Any(x => x.ID == requestUser.ID))
            {
                try
                {
                    string tokenString = requestUser.ID + requestUser.username + userInfo + DateTime.Now.ToLongTimeString();
                    string shaToken = Encryption.Sha256(tokenString);
                    RememberToken token = new RememberToken();
                    token.ID = 0;
                    token.Token = shaToken;
                    token.userID = requestUser.ID;
                    if (_maria.RememberTokens.Any(x => x.Token == shaToken && x.userID == token.userID))
                    {
                        await Clients.Caller.SendAsync("rememberToken", token);
                        return;
                    }
                    if (_maria.RememberTokens.Any(x => x.Token != shaToken && x.userID == token.userID))
                    {
                        _maria.RememberTokens.FirstOrDefault(x => x.userID == token.userID).Token = shaToken;
                        await Clients.Caller.SendAsync("rememberToken", token);
                        await _maria.SaveChangesAsync();
                        return;
                    }
                    await _maria.RememberTokens.AddAsync(token);
                    await _maria.SaveChangesAsync();
                    await Clients.Caller.SendAsync("rememberToken", token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public async Task GetUsers(string token, int userID)
        {
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user != null)
                    user.password = "";
                if (user.userprivileges >= 3)
                {
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.Caller.SendAsync("getusers", users);
                }
            }
        }

        public async Task ChangeUser(User user, string password)
        {
            User changeUser = _maria.Users.FirstOrDefault(x => x.ID == user.ID && password == x.password);
            if (changeUser != null)
            {
                string endMsg = "";
                if (changeUser.username != user.username)
                {
                    if (changeUser.username.Length < 2 || changeUser.username.Length > 20)
                    {
                        await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok" });
                    } else
                    {
                        changeUser.username = user.username;
                        endMsg += "Username";
                    }
                }
                if (user.password.Length > 2 && changeUser.password != user.password)
                {
                    changeUser.password = user.password;
                    endMsg += endMsg.Length > 0 ? " & " : "";
                    endMsg += "Password";
                }
                if (endMsg.Length > 0)
                    endMsg += " successfully changed";
                else
                    endMsg = "Nothing changed.";
                await _maria.SaveChangesAsync();
                await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Success", Question = endMsg, Answer1 = "Ok" });
                List<User> users = _maria.Users.ToList();
                users.ForEach(x => x.password = "");
                await Clients.All.SendAsync("getusers", users);
            } else
            {
                await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "You password was not correct", Answer1 = "Ok" });
            }
        }

        public async Task DeleteUser(string token, int userID, int removeID)
        {
            if (userID == removeID)
            {
                await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "Unable to delete own user", Answer1 = "Ok" });
                return;
            }
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user == null)
                    return;
                if (user.userprivileges >= 3)
                {
                    var removeUser = _maria.Users.ToList().FirstOrDefault(x => x.ID == removeID);
                    if (removeUser != null)
                    {
                        _maria.Users.Remove(removeUser);
                        await _maria.SaveChangesAsync();
                    }
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.All.SendAsync("getusers", users);
                }
            }
        }

        public async Task ApproveUser(string token, int userID, int approveID, bool prove)
        {
            if (userID == approveID)
            {
                await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "Unable to change approve status of own user", Answer1 = "Ok" });
                return;
            }
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user == null)
                    return;
                if (user.userprivileges >= 3)
                {
                    var approveUser = _maria.Users.ToList().FirstOrDefault(x => x.ID == approveID);
                    if (approveUser != null)
                    {
                        approveUser.approved = prove ? 1 : 0;
                        await _maria.SaveChangesAsync();
                    }
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.All.SendAsync("getusers", users);
                }
            }
        }

        public async Task SetUserPrivileges(string token, int userID, int changeID, int privileges)
        {
            if (userID == changeID)
            {
                await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "Unable to change privileges of own user", Answer1 = "Ok" });
                return;
            }
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user == null)
                    return;
                if (user.userprivileges >= 3)
                {
                    var changeUser = _maria.Users.ToList().FirstOrDefault(x => x.ID == changeID);
                    if (changeUser != null)
                    {
                        changeUser.userprivileges = privileges;
                        await _maria.SaveChangesAsync();
                    }
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.All.SendAsync("getusers", users);
                }
            }
        }

        public async Task ValidateToken(string token, int userID)
        {
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user != null)
                    user.password = "";
                await Clients.Caller.SendAsync("userlogin", user);
            }
            else
            {
                await Clients.Caller.SendAsync("userlogin", new User());
            }
        }

        public async Task SendServer(Server server, string UniqueId)
        {
            await Clients.Group(UniqueId).SendAsync("sendserver", server);
        }

        private Room GetRoom(string UniqueId)
        {
            List<Room> Rooms = DataManager.GetRooms();
            Room room = Rooms.FirstOrDefault(x => x.uniqueId == UniqueId);
            if (room == null)
                return null;
            return room;
        }

        public async Task AddVideo(YTVideo key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (!room.server.ytURLs.Select(x => x.url).Contains(key.url))
            {
                if (!key.url.Contains("youtube") && !key.url.Contains("youtu.be")
                    && !key.url.Contains("twitch.tv"))
                {
                    if (key.title.Length == 0)
                        key.title = "External Source";
                    if (key.title == "Vimeo - Video")
                    {
                        VimeoResponse vimeo = Vimeo.FromUrl(key.url);
                        if (vimeo != null)
                        {
                            key.title = vimeo?.Title + (vimeo.UserName == null ? "" : " - " + vimeo.UserName);
                        }
                    }
                }
                else
                {
                    if (key.url.Split('?')[0].ToLower().EndsWith("playlist"))
                    {
                        await AddPlaylist(key.url, UniqueId);
                        return;
                    }
                    if (key.title == null || key.title.Length == 0)
                        key.title = await General.ResolveURL(key.url, UniqueId, Configuration);
                }
                room.server.ytURLs.Add(key);
            }
            if (room.server.ytURL.url.Length == 0 || room.server.ytURL.ended == true)
            {
                room.server.ytURL = key;
                await Clients.Group(UniqueId).SendAsync("videoupdate", key);
            }
            await Clients.Group(UniqueId).SendAsync("playlistupdate", room.server.ytURLs);
            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
        }

        public async Task AddPlaylist(string url, string UniqueId)
        {
            try
            {
                string title, source;
                (title, source) = await General.ResolveTitle(url, 8);
                if (source.Length > 0)
                {
                    HtmlDocument doc = new HtmlDocument();
                    doc.LoadHtml(source);
                    HtmlNode vids = doc.DocumentNode.ChildNodes[1].ChildNodes[1].ChildNodes[15];
                    string json = vids.InnerText.Split(new[] { '=' }, 2)[1].Split(new[] { ';' }, 2)[0];
                    PlaylistInfo playlistInfo = new PlaylistInfo().FromJson(json);
                    var playlist = playlistInfo.Contents.TwoColumnBrowseResultsRenderer.Tabs[0].TabRenderer.Content.SectionListRenderer.Contents[0].ItemSectionRenderer.Contents[0].PlaylistVideoListRenderer.Contents;
                    foreach (var video in playlist)
                    {
                        YTVideo vid = new YTVideo();
                        vid.ended = false;
                        vid.title = video.PlaylistVideoRenderer?.Title.Runs[0].Text;
                        vid.url = "https://www.youtube.com/watch?v=" + video.PlaylistVideoRenderer?.VideoId;
                        await AddVideo(vid, UniqueId).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                await Clients.Caller.SendAsync("dialog", new Dialog() { Header = "Error", Question = "There has been an error trying to add the playlist", Answer1 = "Ok" });
            }
        }

        public async Task RemoveVideo(string key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            int idx = room.server.ytURLs.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                room.server.ytURLs.RemoveAt(idx);
                await Clients.Group(UniqueId).SendAsync("playlistupdate", room.server.ytURLs);
                await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
            }
        }

        public async Task NextVideo(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            if (room.server.ytURLs.Count > 1)
            {
                MainServer.ytURL = room.server.ytURLs[1];
                room.server.ytURLs.RemoveAt(0);
                await Clients.Group(UniqueId).SendAsync("videoupdate", MainServer.ytURL);
                await Clients.Group(UniqueId).SendAsync("playlistupdate", room.server.ytURLs);
                await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
                return;
            }
            else if (room.server.ytURLs.Count == 1)
            {
                MainServer.ytURL.ended = true;
                room.server.ytURLs.RemoveAt(0);
                room.server.isplaying = false;
                room.server.title = "Nothing playing";
                await Clients.Group(UniqueId).SendAsync("isplayingupdate", room.server.isplaying);
                await Clients.Group(UniqueId).SendAsync("playlistupdate", room.server.ytURLs);
                await Clients.Group(UniqueId).SendAsync("sendserver", room.server);
                await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
            }
        }

        public async Task PlayVideo(string key, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.ytURLs.FindIndex(x => x.url == key);
            if (idx != -1)
            {
                YTVideo tempUrl = room.server.ytURLs[idx];
                room.server.ytURLs.RemoveAt(idx);
                room.server.ytURLs.RemoveAt(0);
                room.server.ytURLs.Insert(0, tempUrl);
                MainServer.ytURL = room.server.ytURLs[0];
                await Clients.Group(UniqueId).SendAsync("videoupdate", MainServer.ytURL);
                await Clients.Group(UniqueId).SendAsync("playlistupdate", room.server.ytURLs);
                await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
                return;
            }
        }

        public async Task MoveVideo(int fromIndex, int toIndex, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            YTVideo vid = room.server.ytURLs[fromIndex];
            room.server.ytURLs.RemoveAt(fromIndex);
            room.server.ytURLs.Insert(toIndex, vid);
            await Clients.Group(UniqueId).SendAsync("playlistupdate", room.server.ytURLs);
        }

        public async Task SetTime(double time, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (room.server.ytURL.url.Contains("twitch.tv"))
                await Clients.Group(UniqueId).SendAsync("twitchTimeUpdate", time);
            else
                await Clients.Group(UniqueId).SendAsync("timeupdate", time);
            room.server.currenttime = time;
        }

        public async Task PlayPause(bool isplaying, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            lastPlayPause = DateTime.Now;
            room.server.isplaying = isplaying;
            if (room.server.ytURL.url.Contains("twitch.tv"))
                await Clients.Group(UniqueId).SendAsync("twitchPlaying", isplaying);
            else
                await Clients.Group(UniqueId).SendAsync("isplayingupdate", isplaying);

            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
        }

        public async Task UpdateUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x.username == username);
            if (idx != -1)
            {
                //var address = Context.GetHttpContext().Connection.RemoteIpAddress;
                //var ip = address.ToString();
                var ip = Context.ConnectionId;
                MainServer.members[idx].ip = ip;
                if (MainServer.bannedMembers.Any(x => x.ip == MainServer.members[idx].ip))
                {
                    MainServer.members.RemoveAt(idx);
                    await Clients.Caller.SendAsync("adduserupdate", -2);
                    await Clients.Group(UniqueId).SendAsync("userupdate", MainServer.members);
                    await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
                    return;
                }
                MainServer.members[idx].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                if (MainServer.members.Count == 1 && !MainServer.members[idx].ishost)
                {
                    MainServer.members[idx].ishost = true;
                    await Clients.Group(UniqueId).SendAsync("hostupdate" + MainServer.members[idx].username, true);
                    await Clients.Group(UniqueId).SendAsync("userupdate", MainServer.members);
                }
            }
            else
            {
                await AddUser(username, UniqueId, "");
                return;
            }
        }

        public async Task ChangeHost(string usernameMember, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idxHost = MainServer.members.FindIndex(x => x.ishost == true);
            int idxMember = MainServer.members.FindIndex(x => x.username == usernameMember);
            if (idxHost != -1 && idxMember != -1)
            {
                MainServer.members[idxHost].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxMember].uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss");
                MainServer.members[idxHost].ishost = false;
                MainServer.members[idxMember].ishost = true;
                await Clients.Group(UniqueId).SendAsync("hostupdate" + MainServer.members[idxHost].username, false);
                await Clients.Group(UniqueId).SendAsync("hostupdate" + MainServer.members[idxMember].username, true);
                await Clients.Group(UniqueId).SendAsync("userupdate", MainServer.members);
            }
        }

        public async Task AddRoom(Room room)
        {
            var Rooms = DataManager.GetRooms();
            int RoomCount = 0;
            while (Rooms.Any(x => x.uniqueId == room.uniqueId))
                room.uniqueId = room.uniqueId + RoomCount++;
            Rooms.Add(room);
            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
        }

        public async Task RemoveUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            int idx = MainServer.members.FindIndex(x => x.username == username);
            if (idx == -1)
                return;
            bool isHost = MainServer.members[idx].ishost;
            MainServer.members.RemoveAt(idx);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, UniqueId);
            if (isHost && MainServer.members.Count > 0)
            {
                MainServer.members[0].ishost = true;
                await Clients.Group(UniqueId).SendAsync("hostupdate" + MainServer.members[0].username, true);
            }
            await Clients.Group(UniqueId).SendAsync("userupdate", MainServer.members);
            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
        }

        public async Task RemoveRoom(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            DataManager.GetRooms().Remove(room);
            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
        }

        public async Task AddUser(string username, string UniqueId, string password)
        {
            //var address = Context.GetHttpContext().Connection.LocalIpAddress;
            //var ip = address.ToString();
            var ip = Context.ConnectionId;
            Console.WriteLine(ip);
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            if (room.password != null && room.password != password)
            {
                await Clients.Caller.SendAsync("adduserupdate", -1);
                return;
            }
            Server MainServer = room.server;
            if (MainServer.members == null)
            {
                MainServer.members = new List<Member>();
            }
            Member newMember = new Member() { username = username, ishost = MainServer.members.Count == 0 ? true : false, kick = false, uptime = DateTime.Now.ToString("MM.dd.yyyy HH:mm:ss"), ip = ip };
            if (MainServer.bannedMembers.Any(x => x.ip == newMember.ip))
            {
                await Clients.Caller.SendAsync("adduserupdate", -2);
                return;
            }
            if (MainServer.chatmessages == null)
                MainServer.chatmessages = new List<ChatMessage>();
            await Groups.AddToGroupAsync(Context.ConnectionId, UniqueId);
            MainServer.members.Add(newMember);
            await Clients.Group(UniqueId).SendAsync("userupdate", MainServer.members);
            await Clients.Caller.SendAsync("isplayingupdate", MainServer.isplaying);
            await Clients.Caller.SendAsync("hostupdate" + newMember.username, newMember.ishost);
            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
            await Clients.Caller.SendAsync("adduserupdate", 1);
            if (MainServer.ytURLs.Count > 0)
                await Clients.Caller.SendAsync("playlistupdate", MainServer.ytURLs);
        }

        public async Task BanUser(string username, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            var member = MainServer.members.FirstOrDefault(x => x.username == username);
            if (member != null)
            {
                MainServer.members.Remove(member);
                MainServer.bannedMembers.Add(member);
            }
            await Clients.Group(UniqueId).SendAsync("userupdate", MainServer.members);
            await Clients.All.SendAsync("getrooms", DataManager.GetRooms());
        }

        public async Task SendMessage(ChatMessage message, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            Server MainServer = room.server;
            message.time = DateTime.Now;
            MainServer.chatmessages.Add(message);
            if (MainServer.chatmessages.Count >= 100)
                MainServer.chatmessages = MainServer.chatmessages.GetRange(MainServer.chatmessages.Count - 100, MainServer.chatmessages.Count);
            await Clients.Group(UniqueId).SendAsync("sendmessage", MainServer.chatmessages);
        }

        public async Task DownloadMovie(string url, string filename, string listeningId)
        {
            await Clients.Caller.SendAsync("dlUpdate" + listeningId, "Download will start soon...");
            string fileEnding = "";
            try
            {
                fileEnding = url.Split('.').Last();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }
            string fileDir = System.IO.Directory.GetCurrentDirectory() + "\\movies\\";
            if (!System.IO.Directory.Exists(fileDir))
                System.IO.Directory.CreateDirectory(fileDir);
            string filePath = fileDir + filename + "." + fileEnding;
            using (WebClient wc = new WebClient())
            {
                wc.DownloadFileAsync(
                    new Uri(url),
                    filePath
                );
                wc.DownloadProgressChanged += new DownloadProgressChangedEventHandler((s, e) => _manager.wc_DownloadProgressChanged(s, e, listeningId));
                wc.DownloadFileCompleted += new AsyncCompletedEventHandler((s, e) => _manager.Completed(s, e, listeningId));
            }
            await Clients.Caller.SendAsync("dlUpdate" + listeningId, "Download started");
        }
        public async Task WhiteBoardJoin(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                var drawings = room.server.members.SelectMany(x => x.drawings).OrderBy(x => x.Uuid).ToList();
                if (drawings.Count > 0)
                {
                    //drawings.ForEach(x => x.Uuid = drawings.First().Uuid);
                    await Clients.Caller.SendAsync("whiteboardjoin", drawings);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardUpdate(List<Drawing> updates, string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                room.server.members.First(x => x.ip == Context.ConnectionId).drawings.AddRange(updates);
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).SendAsync("whiteboardupdate", updates);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardClear(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                room.server.members.ForEach(x => x.drawings.Clear());
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).SendAsync("whiteboardclear", true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardUndo(string UniqueId, string UUID)
        {
            try
            {
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).SendAsync("whiteboardundo", UUID);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task WhiteBoardRedo(string UniqueId, string UUID)
        {
            try
            {
                await Clients.GroupExcept(UniqueId, Context.ConnectionId).SendAsync("whiteboardredo", UUID);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}

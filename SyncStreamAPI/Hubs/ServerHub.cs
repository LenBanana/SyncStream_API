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

        private async Task<string> ResolveURL(string url, string UniqueId)
        {
            if (url.Contains("twitch.tv"))
            {
                if ((url.ToLower().StartsWith("http") && url.Count(x => x == '/') == 3) || url.Count(x => x == '/') == 1)
                    return url.Split('/').Last();
                else
                    return "v" + url.Split('/').Last();
            }
            string title = "";
            Uri uri = new Uri(url);
            string videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("v");
            if (videokey == null)
                videokey = System.Web.HttpUtility.ParseQueryString(uri.Query).Get("list");


            if (title.Length == 0)
            {
                try
                {
                    string infoUrl = "http://youtube.com/get_video_info?video_id=" + videokey;
                    using (WebClient client = new WebClient())
                    {
                        string source = "";
                        int i = 0;
                        while ((title.Length == 0 || title.ToLower().Trim() == "youtube") && i < 10)
                        {
                            source = client.DownloadString(infoUrl);
                            if (source.Length > 0)
                            {
                                List<string> attributes = source.Split('&').Select(x => HttpUtility.UrlDecode(x)).ToList();
                                int idx = attributes.FindIndex(x => x.StartsWith("player_response="));
                                if (idx != -1)
                                {
                                    YtVideoInfo videoInfo = new YtVideoInfo().FromJson(attributes[idx].Split(new[] { '=' }, 2)[1]);
                                    return videoInfo.VideoDetails.Title + " - " + videoInfo.VideoDetails.Author;
                                }
                            }
                            await Task.Delay(50);
                            i++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            if (title.Length == 0)
            {
                try
                {
                    var section = Configuration.GetSection("YTKey");
                    string key = section.Value;
                    string Url = "https://www.googleapis.com/youtube/v3/videos?part=snippet&id=" + videokey + "&key=" + key;
                    Ytapi apiResult;
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                    using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
                    using (System.IO.Stream stream = response.GetResponseStream())
                    using (System.IO.StreamReader reader = new System.IO.StreamReader(stream))
                    {
                        apiResult = new Ytapi().FromJson(await reader.ReadToEndAsync());
                    }
                    if (apiResult != null && apiResult.Items.Count > 0)
                        title = apiResult.Items.First().Snippet.Title + " - " + apiResult.Items.First().Snippet.ChannelTitle;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            if (title.Length == 0)
            {
                try
                {
                    string source = "";
                    (title, source) = await General.ResolveTitle(url, 8);
                    if (source.Length > 0)
                    {
                        HtmlDocument doc = new HtmlDocument();
                        doc.LoadHtml(source);
                        HtmlNode vids = doc.DocumentNode.ChildNodes[1].ChildNodes[2].ChildNodes[3];
                        string json = vids.InnerText.Split(new[] { '=' }, 2)[1].Split(new[] { ';' }, 2)[0];
                        PlaylistInfo playlistInfo = new PlaylistInfo().FromJson(json);
                        var playlist = playlistInfo.Contents.TwoColumnBrowseResultsRenderer.Tabs[0].TabRenderer.Content.SectionListRenderer.Contents[0].ItemSectionRenderer.Contents[0].PlaylistVideoListRenderer.Contents;
                        foreach (var video in playlist)
                        {
                            YTVideo vid = new YTVideo();
                            vid.ended = false;
                            vid.title = video.PlaylistVideoRenderer.Title.Runs[0].Text;
                            vid.url = "https://www.youtube.com/watch?v=" + video.PlaylistVideoRenderer.VideoId;
                            await AddVideo(vid, UniqueId).ConfigureAwait(false);
                            await Task.Delay(50);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            return title;
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
                    key.title = await ResolveURL(key.url, UniqueId);
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
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            await Clients.Caller.SendAsync("whiteboardjoin", room.server.drawings);
        }

        public async Task WhiteBoardUpdate(List<object> updates, string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            room.server.drawings.AddRange(updates);
            await Clients.Group(UniqueId).SendAsync("whiteboardupdate", updates);
        }

        public async Task WhiteBoardClear(string UniqueId)
        {
            Room room = GetRoom(UniqueId);
            if (room == null)
                return;
            room.server.drawings.Clear();
            await Clients.Group(UniqueId).SendAsync("whiteboardclear");
        }

        public async Task WhiteBoardUndo(string UniqueId, string UUID)
        {
            await Clients.Group(UniqueId).SendAsync("whiteboardundo", UUID);
        }

        public async Task WhiteBoardRedo(string UniqueId, string UUID)
        {
            await Clients.Group(UniqueId).SendAsync("whiteboardredo", UUID);
        }
    }
}

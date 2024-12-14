using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task dialog(Dialog dialog);

    Task sendserver(Server server);

    Task videoupdate(DreckVideo video);

    Task playlistupdate(List<DreckVideo> videos);

    Task getrooms(IEnumerable<Room> rooms);

    Task isplayingupdate(bool isplaying);

    Task playertype(PlayerType type);

    Task twitchTimeUpdate(double time);

    Task timeupdate(double time);

    Task twitchPlaying(bool playing);

    Task PingTest(DateTime dateSend);
}
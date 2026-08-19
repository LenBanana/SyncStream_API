using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.Wire.Rooms;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task dialog(Dialog dialog);

    Task videoupdate(DreckVideo video);

    Task playlistupdate(List<DreckVideo> videos);

    Task getrooms(IEnumerable<RoomSummaryDto> rooms);

    Task isplayingupdate(bool isplaying);

    Task playertype(PlayerType type);

    Task twitchTimeUpdate(double time);

    Task timeupdate(double time);

    Task twitchPlaying(bool playing);

    Task PingTest(DateTime dateSend);
}
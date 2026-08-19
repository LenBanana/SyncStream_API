using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.Wire.Rooms;
using SyncStreamAPI.Wire.Playback;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task dialog(Dialog dialog);

    Task videoupdate(DreckVideo video);

    Task playlistupdate(List<DreckVideo> videos);

    Task getrooms(IEnumerable<RoomSummaryDto> rooms);

    Task isplayingupdate(bool isplaying);

    Task playertype(PlayerType type);

    Task timeupdate(TimeSyncDto sync);

    Task PingTest(DateTime dateSend);
}
﻿using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task dialog(Dialog dialog);

        Task sendserver(Server server);

        Task videoupdate(DreckVideo video);

        Task playlistupdate(List<DreckVideo> videos);

        Task getrooms(List<Room> rooms);

        Task isplayingupdate(bool isplaying);

        Task twitchTimeUpdate(double time);

        Task timeupdate(double time);

        Task twitchPlaying(bool playing);

        Task PingTest(DateTime dateSend);
        Task savedToDb(string id);
        Task getDownloads(List<FileDto> files);
        Task downloadListen(string id);
        Task downloadRemoved(string id);
        Task downloadProgress(DownloadInfo info);
        Task downloadFinished(string id);

    }
}

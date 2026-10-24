using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper.Streaming;
using SyncStreamAPI.Hubs;
using SyncStreamAPI.Interfaces;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.WebRTC;

namespace SyncStreamAPI.ServerData.Background;

/// <summary>
///     Follows every file the server is streaming to a room: tells the room where playback is and whether it is
///     waiting for the upload, and ends the share once the file has played out or ffmpeg has failed.
/// </summary>
/// <remarks>
///     Nothing used to notice either. At the end of the file the picture froze for everyone and the share stayed
///     open until the sharer stopped it by hand, and a failure looked exactly the same.
/// </remarks>
public class ServerFileShareMonitor : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly WebRtcSfuManager _sfu;
    private readonly RoomMediaService _media;
    private readonly IHubContext<ServerHub, IServerHub> _hub;

    public ServerFileShareMonitor(WebRtcSfuManager sfu, RoomMediaService media, IHubContext<ServerHub, IServerHub> hub)
    {
        _sfu = sfu;
        _media = media;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var room in MainManager.GetRooms().Where(room => room.IsFileSharingActive && room.IsServerFileShare))
            {
                try
                {
                    await FollowAsync(room);
                }
                catch (Exception ex)
                {
                    // An exception escaping ExecuteAsync stops the whole application, and one room's trouble is
                    // no reason to stop following the others. Logged in full and tried again next tick.
                    Console.WriteLine($"[FileShare] following room {room.uniqueId} failed: {ex}");
                }
            }
        }
    }

    private async Task FollowAsync(Room room)
    {
        ServerFileStreamStatus? status;
        try
        {
            status = await _sfu.GetServerFileStreamAsync(SfuRoomId.Stage(room.uniqueId));
        }
        catch (SfuRequestException ex)
        {
            // The SFU is unreachable for the moment. The share is not known to be over, so it is left alone
            // and asked about again on the next tick.
            Console.WriteLine($"[FileShare] could not reach the SFU about room {room.uniqueId}: {ex.Message}");
            return;
        }

        if (status is { IsOver: false })
        {
            await _hub.Clients.Group(room.uniqueId).serverFileShareState(status.PositionSec, status.Buffering);
            return;
        }

        var initiator = room.FileShareInitiator;
        await _media.StopFileShareAsync(room);

        if (status?.State == "failed" && initiator != null)
        {
            await _hub.Clients.Client(initiator).dialog(new Dialog(AlertType.Danger)
            {
                Header = "File share stopped",
                Question = $"The server could not keep streaming the file: {status.Error}",
                Answer1 = "Ok"
            });
        }
    }
}

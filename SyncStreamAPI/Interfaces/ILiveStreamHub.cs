using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.Wire.Users;
using SyncStreamAPI.Wire.Live;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task getliveusers(List<LiveUserDto> user);
    Task getwatchingusers(List<UserSummaryDto> user);
}
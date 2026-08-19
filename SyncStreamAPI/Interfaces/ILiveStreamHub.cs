using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task getliveusers(List<LiveUserDTO> user);
    Task getwatchingusers(List<UserSummaryDto> user);
}
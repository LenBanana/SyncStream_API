using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task getliveusers(List<LiveUserDTO> user);
    Task getwatchingusers(List<UserDTO> user);
}
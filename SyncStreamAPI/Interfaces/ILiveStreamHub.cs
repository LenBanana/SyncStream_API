using SyncStreamAPI.Models;
using SyncStreamAPI.Models.RTMP;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task getliveusers(List<LiveUserDTO> user);
        Task getwatchingusers(List<UserDTO> user);
    }
}

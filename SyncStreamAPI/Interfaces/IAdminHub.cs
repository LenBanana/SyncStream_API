using SyncStreamAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models.ServerHealth;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task getPrivilegeInfo(List<PrivilegeInfo> info);
        Task userlogin(UserDTO user);

        Task userRegister(UserDTO user);

        Task rememberToken(RememberTokenDTO token);

        Task getusers(List<UserDTO> users);
        Task serverHealth(ServerHealthDto healthDto);
    }
}

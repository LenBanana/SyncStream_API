using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task userlogin(UserDTO user);

        Task userRegister(UserDTO user);

        Task rememberToken(RememberTokenDTO token);

        Task getusers(List<UserDTO> users);
    }
}

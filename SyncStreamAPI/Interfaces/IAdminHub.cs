using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task userlogin(User user);

        Task userRegister(User user);

        Task rememberToken(RememberToken token);

        Task getusers(List<User> users);
    }
}

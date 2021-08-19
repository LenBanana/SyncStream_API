using SyncStreamAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task adduserupdate(int errorCode);

        Task userupdate(List<Member> members);

        Task hostupdate(bool isHost);
    }
}

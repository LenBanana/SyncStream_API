using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task adduserupdate(int errorCode);

        Task userupdate(List<MemberDTO> members);

        Task hostupdate(bool isHost);
    }
}

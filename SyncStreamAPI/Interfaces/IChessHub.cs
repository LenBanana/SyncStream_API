using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Members;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task playchess();
    }
}

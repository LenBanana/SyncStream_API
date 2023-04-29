using SyncStreamAPI.Models.MediaModels;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task mediaStatus(EditorProcess dialog);
        Task finishStatus(EditorProcess dialog);
    }
}

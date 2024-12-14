using System.Threading.Tasks;
using SyncStreamAPI.Models.MediaModels;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task mediaStatus(EditorProcess dialog);
    Task finishStatus(EditorProcess dialog);
}
using SyncStreamAPI.DTOModel;
using SyncStreamAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task whiteboardjoin(List<Drawing> drawings);

        Task whiteboardupdate(List<Drawing> newDrawings);

        Task whiteboardclear(bool clear);

        Task whiteboardundo(string UUID);

        Task whiteboardredo(string UUID);

        Task playinggallows(string word);

        Task gallowusers(List<MemberDTO> members);
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Members;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task whiteboardjoin(List<Drawing> drawings);

    Task whiteboardupdate(List<Drawing> newDrawings);

    Task whiteboardclear(bool clear);

    Task whiteboardundo(string UUID);

    Task whiteboardredo(string UUID);

    Task isdrawingupdate(bool isDrawing);

    Task playinggallows(string word);

    Task gallowusers(List<GallowMember> members);

    Task gallowtimerupdate(int time);

    Task gallowtimerelapsed(int time);
}
using Microsoft.AspNetCore.SignalR;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.GameModels.Gallows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task PlayGallows(string UniqueId)
        {
            try
            {
                Room room = GetRoom(UniqueId);
                if (room == null)
                    return;
                await Clients.Group(UniqueId).gallowusers(room.GallowGame.members);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}

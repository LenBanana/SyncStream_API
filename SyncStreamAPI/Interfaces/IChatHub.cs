using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Interfaces
{
    public partial interface IServerHub
    {
        Task sendmessage(ChatMessage chatMessages);

        Task sendmessages(List<ChatMessage> chatMessages);

        Task PrivateMessage(string message);
    }
}

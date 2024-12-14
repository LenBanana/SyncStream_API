using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task sendmessage(ChatMessage chatMessages);

    Task sendmessages(List<ChatMessage> chatMessages);

    Task PrivateMessage(string message);
}
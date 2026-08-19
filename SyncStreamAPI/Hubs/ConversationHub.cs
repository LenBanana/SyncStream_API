using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using Microsoft.AspNetCore.Authorization;
using SyncStreamAPI.Auth;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Authorize(Policy = AuthorizationPolicies.Approved)]
    public async Task PrivateChat(int receiverId, string message)
    {
        var dbUser = _postgres.Users?.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.ID == CallerUserId);
        var receiver = _postgres.Users?.FirstOrDefault(x => x.ID == receiverId);
        if (dbUser == null || receiver == null) return;
        var conversation = new DbConversation(dbUser.ID, receiver.ID, message);
        _postgres.Conversations.Add(conversation);
        await _postgres.SaveChangesAsync();
        await Clients.Caller.dialog(new Dialog(AlertType.Success)
            { Header = "Success", Question = "Message sent", Answer1 = "Ok" });
        await Clients.Group(receiverId.ToString()).dialog(new Dialog
            { Header = "New Message", Question = $"You have a new message from {dbUser.username}", Answer1 = "Ok" });
    }
}
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Privilege(RequiredPrivileges = UserPrivileges.Approved, AuthenticationType = AuthenticationType.Token)]
    public async Task PrivateChat(string token, int receiverId, string message)
    {
        var dbUser = _postgres.Users?.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.RememberTokens.FirstOrDefault(y => y.Token == token) != null);
        var receiver = _postgres.Users?.FirstOrDefault(x => x.ID == receiverId);
        if (dbUser == null || receiver == null)
        {
            return;
        }
        var conversation = new DbConversation(dbUser.ID, receiver.ID, message);
        _postgres.Conversations.Add(conversation);
        await _postgres.SaveChangesAsync();
        await Clients.Caller.dialog(new Dialog(AlertType.Success) { Header = "Success", Question = "Message sent", Answer1 = "Ok" });
        await Clients.Group(receiverId.ToString()).dialog(new Dialog(AlertType.Info) { Header = "New Message", Question = $"You have a new message from {dbUser.username}", Answer1 = "Ok" });
    }
}
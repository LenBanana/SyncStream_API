using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Privilege(RequiredPrivileges = UserPrivileges.Elevated, AuthenticationType = AuthenticationType.Token)]
    public async Task PublicAnnouncement(string token, string message, AlertType alertType)
    {
        await Clients.Others.dialog(new Dialog(alertType)
            { Header = "Server Announcement", Question = message, Answer1 = "Ok" });
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Elevated, AuthenticationType = AuthenticationType.Token)]
    public async Task GetPermissions(string token)
    {
        var privilegeInfos = PrivilegeInfo.GetPrivilegedMethodsInfo();
        await Clients.Caller.getPrivilegeInfo(privilegeInfos);
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task GetUsers(string token, int userId)
    {
        var users = _postgres.Users.ToList();
        await Clients.Caller.getusers(users?.Select(x => x.ToDTO()).ToList());
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task DeleteUser(string token, int userId, int removeId)
    {
        if (userId == removeId)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Unable to delete own user", Answer1 = "Ok" });
            return;
        }

        var dbUser = _postgres.Users?.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.RememberTokens.FirstOrDefault(y => y.Token == token) != null);
        var removeUser = _postgres.Users?.ToList().FirstOrDefault(x => x.ID == removeId);
        if (dbUser != null && removeUser != null && dbUser.userprivileges > removeUser.userprivileges)
        {
            _postgres.Users.Remove(removeUser);
            await _postgres.SaveChangesAsync();
        }

        if (_postgres.Users != null)
        {
            var users = _postgres.Users.ToList();
            await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
        }
    }

    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task GenerateApiKey(string token)
    {
        var dbUser = _postgres.Users?.Include(x => x.RememberTokens)
            .FirstOrDefault(x => x.RememberTokens.FirstOrDefault(y => y.Token == token) != null);
        if (dbUser != null)
        {
            dbUser.ApiKey = dbUser.GenerateStreamToken().Token;
            await _postgres.SaveChangesAsync();
            await Clients.Caller.userlogin(dbUser.ToDTO());
        }
    }

    [ErrorHandling]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task ApproveUser(string token, int userId, int approveId, bool approve)
    {
        if (userId == approveId)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Unable to change approve status of own user", Answer1 = "Ok" });
            return;
        }

        var dbUser = _postgres.Users?.Where(x => x.ID == userId).Include(x => x.RememberTokens).FirstOrDefault();
        var dbToken = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
        if (dbToken != null)
        {
            if (dbUser.userprivileges >= UserPrivileges.Administrator)
            {
                var approveUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == approveId);
                if (approveUser != null && (dbUser.userprivileges > approveUser.userprivileges ||
                                            dbUser.userprivileges == UserPrivileges.Elevated))
                {
                    approveUser.approved = approve ? 1 : 0;
                    if (approveUser.userprivileges == UserPrivileges.NotApproved && approve)
                        approveUser.userprivileges = UserPrivileges.Approved;

                    if (approveUser.userprivileges != UserPrivileges.NotApproved && !approve)
                        approveUser.userprivileges = UserPrivileges.NotApproved;

                    await _postgres.SaveChangesAsync();
                }
                else
                {
                    await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                        { Header = "Error", Question = "Insufficient permissions", Answer1 = "Ok" });
                }

                var users = _postgres.Users.ToList();
                await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
            }
            else
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                    { Header = "Error", Question = "Insufficient permissions", Answer1 = "Ok" });
            }
        }
    }

    [ErrorHandling]
    [Privilege(RequiredPrivileges = UserPrivileges.Administrator, AuthenticationType = AuthenticationType.Token)]
    public async Task SetUserPrivileges(string token, int userId, int changeId, int privileges)
    {
        if (userId == changeId) throw new Exception("Unable to change own user");

        var dbUsers = _postgres.Users
            .Include(u => u.RememberTokens)
            .Where(u => u.ID == userId || u.ID == changeId)
            .ToList();
        var dbUser = dbUsers.FirstOrDefault(u => u.ID == userId && u.RememberTokens.Any(x => x.Token == token));
        var changeUser = dbUsers.FirstOrDefault(u => u.ID == changeId);
        if (dbUser == null) throw new Exception("User not found");

        if (changeUser == null) throw new Exception("User to be changed not found");

        if (dbUser.userprivileges < UserPrivileges.Administrator ||
            dbUser.userprivileges <= changeUser.userprivileges)
            throw new UnauthorizedAccessException("Insufficient permissions");

        changeUser.userprivileges = (UserPrivileges)privileges;
        changeUser.approved = changeUser.userprivileges == UserPrivileges.NotApproved ? 0 : 1;
        await _postgres.SaveChangesAsync();
        var users = _postgres.Users.ToList();
        await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
    }
}
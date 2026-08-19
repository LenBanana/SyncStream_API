using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Auth;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Wire;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [Authorize(Policy = AuthorizationPolicies.Elevated)]
    public async Task PublicAnnouncement(string message, AlertType alertType)
    {
        await Clients.Others.dialog(new Dialog(alertType)
            { Header = "Server Announcement", Question = message, Answer1 = "Ok" });
    }

    [Authorize(Policy = AuthorizationPolicies.Elevated)]
    public async Task GetPermissions()
    {
        var privilegeInfos = PrivilegeInfo.GetPrivilegedMethodsInfo();
        await Clients.Caller.getPrivilegeInfo(privilegeInfos);
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task GetUsers()
    {
        var users = _postgres.Users.ToList();
        await Clients.Caller.getusers(users.ToSummaries());
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task DeleteUser(int removeId)
    {
        var actor = await GetCallerUserAsync();
        if (actor == null) return;

        if (actor.ID == removeId)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Unable to delete own user", Answer1 = "Ok" });
            return;
        }

        var removeUser = _postgres.Users.FirstOrDefault(x => x.ID == removeId);
        if (removeUser == null) return;

        if (actor.userprivileges <= removeUser.userprivileges)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Insufficient permissions", Answer1 = "Ok" });
            return;
        }

        _postgres.Users.Remove(removeUser);
        await _postgres.SaveChangesAsync();
        await Clients.All.getusers(_postgres.Users.ToList().ToSummaries());
    }

    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task GenerateApiKey()
    {
        var actor = await GetCallerUserAsync();
        if (actor == null) return;

        actor.ApiKey = RememberTokens.Issue().Token;
        await _postgres.SaveChangesAsync();
        await Clients.Caller.userlogin(actor.ToSelfProfile());
    }

    [ErrorHandling]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task ApproveUser(int approveId, bool approve)
    {
        var actor = await GetCallerUserAsync();
        if (actor == null) return;

        if (actor.ID == approveId)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
            {
                Header = "Error", Question = "Unable to change approve status of own user", Answer1 = "Ok"
            });
            return;
        }

        var approveUser = _postgres.Users.FirstOrDefault(x => x.ID == approveId);
        var mayChange = approveUser != null
                        && (actor.userprivileges > approveUser.userprivileges
                            || actor.userprivileges == UserPrivileges.Elevated);

        if (!mayChange)
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Insufficient permissions", Answer1 = "Ok" });
            return;
        }

        approveUser!.approved = approve ? 1 : 0;
        if (approveUser.userprivileges == UserPrivileges.NotApproved && approve)
            approveUser.userprivileges = UserPrivileges.Approved;

        if (approveUser.userprivileges != UserPrivileges.NotApproved && !approve)
            approveUser.userprivileges = UserPrivileges.NotApproved;

        await _postgres.SaveChangesAsync();
        await Clients.All.getusers(_postgres.Users.ToList().ToSummaries());
    }

    [ErrorHandling]
    [Authorize(Policy = AuthorizationPolicies.Administrator)]
    public async Task SetUserPrivileges(int changeId, int privileges)
    {
        var actor = await GetCallerUserAsync();
        if (actor == null) throw new UnauthorizedAccessException("Not signed in");

        if (actor.ID == changeId) throw new Exception("Unable to change own user");

        var changeUser = _postgres.Users.FirstOrDefault(u => u.ID == changeId);
        if (changeUser == null) throw new Exception("User to be changed not found");

        if (actor.userprivileges < UserPrivileges.Administrator ||
            actor.userprivileges <= changeUser.userprivileges)
            throw new UnauthorizedAccessException("Insufficient permissions");

        changeUser.userprivileges = (UserPrivileges)privileges;
        changeUser.approved = changeUser.userprivileges == UserPrivileges.NotApproved ? 0 : 1;
        await _postgres.SaveChangesAsync();
        await Clients.All.getusers(_postgres.Users.ToList().ToSummaries());
    }
}

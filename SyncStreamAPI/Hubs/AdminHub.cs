using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Enums;
using System.Text.RegularExpressions;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task LoginRequest(DbUser requestUser, string userInfo)
        {
            var result = new DbUser();
            DbUser user = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x => x.username == requestUser.username && x.password == requestUser.password);
            if (user != null)
            {
                if (user.approved <= 0)
                {
                    await Clients.Caller.dialog(new Dialog(AlertTypes.Info) { Header = "Thanks for registering", Question = @$"Please wait until the admin team approves your account", Answer1 = "Ok" });
                    return;
                }
                _manager.AddMember(user.ID, Context.ConnectionId);
                var token = user.GenerateToken(userInfo);
                var dbToken = user.RememberTokens.FirstOrDefault(x => x.Token == token.Token);
                if (dbToken == null)
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, token.Token);
                    user.RememberTokens.Add(token);
                    await Clients.Caller.rememberToken(new RememberTokenDTO(token, user.ID));
                    await _postgres.SaveChangesAsync();
                }
                else
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, dbToken.Token);
                    await Clients.Caller.rememberToken(new RememberTokenDTO(dbToken, user.ID));
                }
                await Groups.AddToGroupAsync(Context.ConnectionId, General.LoggedInGroupName);
                result = user;
            }
            await Clients.Caller.userlogin(result.ToDTO());
        }

        public async Task RegisterRequest(DbUser requestUser)
        {
            var result = new DbUser();
            if (_postgres.Users?.Any(x => x.username == requestUser.username) == false)
            {
                var userNameRegexString = @"^\w(?:\w|[.-](?=\w)){2,31}$";
                var userNameRegex = new Regex(userNameRegexString);
                if (!userNameRegex.IsMatch(requestUser.username))
                {
                    await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = @$"Username {requestUser.username} not allowed", Answer1 = "Ok" });
                    return;
                }
                requestUser.StreamToken = requestUser.GenerateStreamToken().Token;
                await _postgres.Users.AddAsync(requestUser);
                await _postgres.SaveChangesAsync();
                result = requestUser;
                List<DbUser> users = _postgres.Users.ToList();
                await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
            }
            await Clients.Caller.userRegister(result.ToDTO());
        }

        public async Task GenerateRememberToken(DbUser requestUser, string userInfo)
        {
            try
            {
                var dbUser = _postgres.Users.FirstOrDefault(x => x.ID == requestUser.ID);
                var token = dbUser?.GenerateToken(userInfo);
                if (requestUser?.RememberTokens.Any(x => x.Token == token?.Token) == true)
                {
                    await Clients.Caller.rememberToken(new RememberTokenDTO(token, requestUser.ID));
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public async Task GetUsers(string token, int userID)
        {
            var dbUser = _postgres.Users?.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser != null)

                    if (dbUser.userprivileges >= UserPrivileges.Administrator)
                    {
                        List<DbUser> users = _postgres.Users.ToList();
                        await Clients.Caller.getusers(users?.Select(x => x.ToDTO()).ToList());
                    }
            }
        }

        public async Task ChangeUser(DbUser user, string password)
        {
            DbUser changeUser = _postgres.Users.FirstOrDefault(x => x.ID == user.ID && password == x.password);
            if (changeUser != null)
            {
                string endMsg = "";
                if (changeUser.username != user.username)
                {
                    if (changeUser.username.Length < 2 || changeUser.username.Length > 20)
                    {
                        await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok" });
                    }
                    else
                    {
                        changeUser.username = user.username;
                        endMsg += "Username";
                    }
                }
                if (user.password.Length > 2 && changeUser.password != user.password)
                {
                    changeUser.password = user.password;
                    endMsg += endMsg.Length > 0 ? " & " : "";
                    endMsg += "Password";
                }
                if (endMsg.Length > 0)
                    endMsg += " successfully changed";
                else
                    endMsg = "Nothing changed.";
                await _postgres.SaveChangesAsync();
                await Clients.Caller.dialog(new Dialog() { Header = "Success", Question = endMsg, Answer1 = "Ok" });
                List<DbUser> users = _postgres.Users.ToList();
                await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
            }
            else
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "You password was not correct", Answer1 = "Ok" });
            }
        }

        public async Task DeleteUser(string token, int userID, int removeID)
        {
            if (userID == removeID)
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "Unable to delete own user", Answer1 = "Ok" });
                return;
            }
            var dbUser = _postgres.Users?.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var removeUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == removeID);
                    if (removeUser != null)
                    {
                        _postgres.Users.Remove(removeUser);
                        await _postgres.SaveChangesAsync();
                    }
                    List<DbUser> users = _postgres.Users.ToList();
                    await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
                }
            }
        }

        public async Task GenerateApiKey(string token)
        {
            var dbUser = _postgres.Users?.Include(x => x.RememberTokens).FirstOrDefault(x => x.RememberTokens.FirstOrDefault(y => y.Token == token) != null);
            if (dbUser != null)
            {
                if (dbUser.userprivileges >= UserPrivileges.Administrator && (dbUser.ApiKey == null || dbUser.ApiKey.Length == 0))
                {
                    dbUser.ApiKey = dbUser.GenerateStreamToken().Token;
                    await _postgres.SaveChangesAsync();
                    await Clients.Caller.userlogin(dbUser.ToDTO());
                }
            }
        }

        public async Task ApproveUser(string token, int userID, int approveID, bool approve)
        {
            if (userID == approveID)
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "Unable to change approve status of own user", Answer1 = "Ok" });
                return;
            }
            var dbUser = _postgres.Users?.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= UserPrivileges.Administrator)
                {
                    var approveUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == approveID);
                    if (approveUser != null && (dbUser.userprivileges > approveUser.userprivileges || dbUser.userprivileges == UserPrivileges.Elevated))
                    {
                        approveUser.approved = approve ? 1 : 0;
                        if (approveUser.userprivileges == UserPrivileges.NotApproved && approve)
                            approveUser.userprivileges = UserPrivileges.Approved;
                        if (approveUser.userprivileges != UserPrivileges.NotApproved && !approve)
                            approveUser.userprivileges = UserPrivileges.NotApproved;
                        await _postgres.SaveChangesAsync();
                    }
                    else
                        await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "Insufficient permissions", Answer1 = "Ok" });
                    List<DbUser> users = _postgres.Users.ToList();
                    await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
                }
                else
                    await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = "Insufficient permissions", Answer1 = "Ok" });
            }
        }

        public async Task SetUserPrivileges(string token, int userID, int changeID, int privileges)
        {
            try
            {
                if (userID == changeID)
                    throw new Exception("Unable to change own user");
                var dbUser = _postgres.Users?.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
                DbRememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
                if (Token != null)
                {
                    if (dbUser == null)
                        return;
                    if (dbUser.userprivileges >= UserPrivileges.Administrator && (dbUser.userprivileges > (UserPrivileges)privileges || dbUser.userprivileges == UserPrivileges.Elevated))
                    {
                        var changeUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == changeID);
                        if (changeUser != null && dbUser.userprivileges > changeUser.userprivileges)
                        {
                            changeUser.userprivileges = (UserPrivileges)privileges;
                            if (changeUser.userprivileges == UserPrivileges.NotApproved)
                                changeUser.approved = 0;
                            if (changeUser.approved == 0 && changeUser.userprivileges > UserPrivileges.NotApproved)
                                changeUser.approved = 1;
                            await _postgres.SaveChangesAsync();
                        }
                        else
                            throw new UnauthorizedAccessException("Insufficient permissions");
                    }
                    else
                        throw new UnauthorizedAccessException("Insufficient permissions");
                }
                else
                    throw new Exception("User not found");
            }
            catch (UnauthorizedAccessException ex)
            {
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                await Clients.Caller.dialog(new Dialog(AlertTypes.Danger) { Header = "Error", Question = ex.Message, Answer1 = "Ok" });
            }
            List<DbUser> users = _postgres.Users.ToList();
            await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
        }

        public async Task ValidateToken(string token, int userID)
        {
            try
            {
                var dbUser = _postgres.Users?.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
                if (dbUser == null || (dbUser?.RememberTokens.Any(x => x.Token == token) == false))
                {
                    await Clients.Caller.userlogin(new DbUser("").ToDTO());
                    return;
                }
                if (dbUser.approved <= 0)
                {
                    await Clients.Caller.dialog(new Dialog(AlertTypes.Info) { Header = "Thanks for registering", Question = @$"Please wait until the admin team approves your account", Answer1 = "Ok" });
                    return;
                }
                dbUser.RememberTokens = dbUser.RememberTokens.GroupBy(x => x.Token)?.Select(x => x.First()).ToList();
                foreach (var t in dbUser.RememberTokens.ToList())
                {
                    if ((DateTime.Now - t.Created).TotalDays > 30)
                    {
                        dbUser.RememberTokens.Remove(t);
                        _postgres.RememberTokens.Remove(t);
                    }
                }
                if (dbUser.StreamToken == null)
                    dbUser.StreamToken = dbUser.GenerateStreamToken().Token;
                DbRememberToken Token = dbUser.RememberTokens.FirstOrDefault(x => x.Token == token);
                if (Token != null)
                {
                    _manager.AddMember(dbUser.ID, Context.ConnectionId);
                    await Groups.AddToGroupAsync(Context.ConnectionId, Token.Token);
                    await Groups.AddToGroupAsync(Context.ConnectionId, General.LoggedInGroupName);
                    await Clients.Caller.userlogin(dbUser.ToDTO());
                    Token.Created = DateTime.Now;
                }
                else
                {
                    await Clients.Caller.userlogin(new DbUser("").ToDTO());
                }
                await _postgres.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                await Clients.Caller.userlogin(new DbUser("").ToDTO());
                Console.WriteLine(ex.ToString());
            }
        }
    }
}

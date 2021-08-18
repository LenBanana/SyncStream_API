using SyncStreamAPI.Helper;
using SyncStreamAPI.MariaModels;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task LoginRequest(User requestUser)
        {
            User user = _maria.Users.FirstOrDefault(x => x.username == requestUser.username && x.password == requestUser.password);
            if (user != null)
                user.password = "";
            await Clients.Caller.userlogin(user);
        }

        public async Task RegisterRequest(User requestUser)
        {
            User user = null;
            if (!_maria.Users.Any(x => x.username == requestUser.username))
            {
                if (requestUser.username.Length < 2 || requestUser.username.Length > 20)
                {
                    await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok" });
                    return;
                }
                await _maria.Users.AddAsync(requestUser);
                await _maria.SaveChangesAsync();
                user = requestUser;
                user.password = "";
            }
            await Clients.Caller.userRegister(user);
        }

        public async Task GenerateRememberToken(User requestUser, string userInfo)
        {
            if (_maria.Users.Any(x => x.ID == requestUser.ID))
            {
                try
                {
                    string tokenString = requestUser.ID + requestUser.username + userInfo + DateTime.Now.ToLongTimeString();
                    string shaToken = Encryption.Sha256(tokenString);
                    RememberToken token = new RememberToken();
                    token.ID = 0;
                    token.Token = shaToken;
                    token.userID = requestUser.ID;
                    if (_maria.RememberTokens.Any(x => x.Token == shaToken && x.userID == token.userID))
                    {
                        await Clients.Caller.rememberToken(token);
                        return;
                    }
                    if (_maria.RememberTokens.Any(x => x.Token != shaToken && x.userID == token.userID))
                    {
                        _maria.RememberTokens.FirstOrDefault(x => x.userID == token.userID).Token = shaToken;
                        await Clients.Caller.rememberToken(token);
                        await _maria.SaveChangesAsync();
                        return;
                    }
                    await _maria.RememberTokens.AddAsync(token);
                    await _maria.SaveChangesAsync();
                    await Clients.Caller.rememberToken(token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        public async Task GetUsers(string token, int userID)
        {
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user != null)
                    user.password = "";
                if (user.userprivileges >= 3)
                {
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.Caller.getusers(users);
                }
            }
        }

        public async Task ChangeUser(User user, string password)
        {
            User changeUser = _maria.Users.FirstOrDefault(x => x.ID == user.ID && password == x.password);
            if (changeUser != null)
            {
                string endMsg = "";
                if (changeUser.username != user.username)
                {
                    if (changeUser.username.Length < 2 || changeUser.username.Length > 20)
                    {
                        await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok" });
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
                await _maria.SaveChangesAsync();
                await Clients.Caller.dialog(new Dialog() { Header = "Success", Question = endMsg, Answer1 = "Ok" });
                List<User> users = _maria.Users.ToList();
                users.ForEach(x => x.password = "");
                await Clients.All.getusers(users);
            }
            else
            {
                await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "You password was not correct", Answer1 = "Ok" });
            }
        }

        public async Task DeleteUser(string token, int userID, int removeID)
        {
            if (userID == removeID)
            {
                await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "Unable to delete own user", Answer1 = "Ok" });
                return;
            }
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user == null)
                    return;
                if (user.userprivileges >= 3)
                {
                    var removeUser = _maria.Users.ToList().FirstOrDefault(x => x.ID == removeID);
                    if (removeUser != null)
                    {
                        _maria.Users.Remove(removeUser);
                        await _maria.SaveChangesAsync();
                    }
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.All.getusers(users);
                }
            }
        }

        public async Task ApproveUser(string token, int userID, int approveID, bool prove)
        {
            if (userID == approveID)
            {
                await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "Unable to change approve status of own user", Answer1 = "Ok" });
                return;
            }
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user == null)
                    return;
                if (user.userprivileges >= 3)
                {
                    var approveUser = _maria.Users.ToList().FirstOrDefault(x => x.ID == approveID);
                    if (approveUser != null)
                    {
                        approveUser.approved = prove ? 1 : 0;
                        await _maria.SaveChangesAsync();
                    }
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.All.getusers(users);
                }
            }
        }

        public async Task SetUserPrivileges(string token, int userID, int changeID, int privileges)
        {
            if (userID == changeID)
            {
                await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "Unable to change privileges of own user", Answer1 = "Ok" });
                return;
            }
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user == null)
                    return;
                if (user.userprivileges >= 3)
                {
                    var changeUser = _maria.Users.ToList().FirstOrDefault(x => x.ID == changeID);
                    if (changeUser != null)
                    {
                        changeUser.userprivileges = privileges;
                        await _maria.SaveChangesAsync();
                    }
                    List<User> users = _maria.Users.ToList();
                    users.ForEach(x => x.password = "");
                    await Clients.All.getusers(users);
                }
            }
        }

        public async Task ValidateToken(string token, int userID)
        {
            RememberToken Token = _maria.RememberTokens.FirstOrDefault(x => x.Token == token && x.userID == userID);
            if (Token != null)
            {
                User user = _maria.Users.FirstOrDefault(x => x.ID == Token.userID);
                if (user != null)
                    user.password = "";
                await Clients.Caller.userlogin(user);
            }
            else
            {
                await Clients.Caller.userlogin(new User());
            }
        }
    }
}

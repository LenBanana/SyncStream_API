using SyncStreamAPI.Helper;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SyncStreamAPI.Hubs
{
    public partial class ServerHub
    {
        public async Task LoginRequest(User requestUser, string userInfo)
        {
            var result = new User();
            User user = _postgres.Users.FirstOrDefault(x => x.username == requestUser.username && x.password == requestUser.password);
            if (user != null)
            {
                var token = user.GenerateToken(userInfo);
                var dbToken = user.RememberTokens.FirstOrDefault(x => x.Token == token.Token);
                if (dbToken == null)
                {
                    user.RememberTokens.Add(token);
                    await Clients.Caller.rememberToken(new RememberTokenDTO(token, user.ID));
                    await _postgres.SaveChangesAsync();
                }
                else
                {
                    await Clients.Caller.rememberToken(new RememberTokenDTO(dbToken, user.ID));
                }

                result = user;
            }
            await Clients.Caller.userlogin(result.ToDTO());
        }

        public async Task RegisterRequest(User requestUser)
        {
            var result = new User();
            if (!_postgres.Users.Any(x => x.username == requestUser.username))
            {
                if (requestUser.username.Length < 2 || requestUser.username.Length > 20)
                {
                    await Clients.Caller.dialog(new Dialog() { Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok" });
                    return;
                }
                await _postgres.Users.AddAsync(requestUser);
                await _postgres.SaveChangesAsync();
                result = requestUser;

            }
            await Clients.Caller.userRegister(result.ToDTO());
        }

        public async Task GenerateRememberToken(User requestUser, string userInfo)
        {
            try
            {
                var dbUser = _postgres.Users.First(x => x.ID == requestUser.ID);
                var token = dbUser.GenerateToken(userInfo);
                if (requestUser.RememberTokens.Any(x => x.Token == token.Token))
                {
                    await Clients.Caller.rememberToken(new RememberTokenDTO(token, requestUser.ID));
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        public async Task GetUsers(string token, int userID)
        {
            var dbUser = _postgres.Users.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser != null)

                    if (dbUser.userprivileges >= 3)
                    {
                        List<User> users = _postgres.Users.ToList();
                        await Clients.Caller.getusers(users.Select(x => x.ToDTO()).ToList());
                    }
            }
        }

        public async Task ChangeUser(User user, string password)
        {
            User changeUser = _postgres.Users.FirstOrDefault(x => x.ID == user.ID && password == x.password);
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
                await _postgres.SaveChangesAsync();
                await Clients.Caller.dialog(new Dialog() { Header = "Success", Question = endMsg, Answer1 = "Ok" });
                List<User> users = _postgres.Users.ToList();
                await Clients.All.getusers(users.Select(x => x.ToDTO()).ToList());
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
            var dbUser = _postgres.Users.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= 3)
                {
                    var removeUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == removeID);
                    if (removeUser != null)
                    {
                        _postgres.Users.Remove(removeUser);
                        await _postgres.SaveChangesAsync();
                    }
                    List<User> users = _postgres.Users.ToList();
                    await Clients.All.getusers(users.Select(x => x.ToDTO()).ToList());
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
            var dbUser = _postgres.Users.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= 3)
                {
                    var approveUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == approveID);
                    if (approveUser != null)
                    {
                        approveUser.approved = prove ? 1 : 0;
                        await _postgres.SaveChangesAsync();
                    }
                    List<User> users = _postgres.Users.ToList();
                    await Clients.All.getusers(users.Select(x => x.ToDTO()).ToList());
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
            var dbUser = _postgres.Users.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            RememberToken Token = dbUser?.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                if (dbUser == null)
                    return;
                if (dbUser.userprivileges >= 3)
                {
                    var changeUser = _postgres.Users.ToList().FirstOrDefault(x => x.ID == changeID);
                    if (changeUser != null)
                    {
                        changeUser.userprivileges = privileges;
                        await _postgres.SaveChangesAsync();
                    }
                    List<User> users = _postgres.Users.ToList();
                    await Clients.All.getusers(users.Select(x => x.ToDTO()).ToList());
                }
            }
        }

        public async Task ValidateToken(string token, int userID)
        {
            var dbUser = _postgres.Users.Where(x => x.ID == userID && x.RememberTokens.Any(y => y.Token == token)).Include(x => x.RememberTokens).FirstOrDefault();
            if (dbUser == null)
            {
                await Clients.Caller.userlogin(new User("").ToDTO());
                return;
            }
            foreach (var t in dbUser.RememberTokens)
            {
                if ((DateTime.Now - t.Created).TotalDays > 30)
                {
                    dbUser.RememberTokens.Remove(t);
                    _postgres.RememberTokens.Remove(t);
                }
            }
            RememberToken Token = dbUser.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                await Clients.Caller.userlogin(dbUser.ToDTO());
                Token.Created = DateTime.Now;
            }
            else
            {
                await Clients.Caller.userlogin(new User("").ToDTO());
            }
            await _postgres.SaveChangesAsync();
        }
    }
}

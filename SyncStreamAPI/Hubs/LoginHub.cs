﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncStreamAPI.Annotations;
using SyncStreamAPI.Enums;
using SyncStreamAPI.Helper;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using SyncStreamAPI.ServerData;

namespace SyncStreamAPI.Hubs;

public partial class ServerHub
{
    [ErrorHandling]
    public async Task LoginRequest(DbUser requestUser, string userInfo)
    {
        var result = new DbUser();
        var user = _postgres.Users.Include(x => x.RememberTokens).FirstOrDefault(x =>
            x.username == requestUser.username && x.password == requestUser.password);
        if (user != null)
        {
            if (user.approved <= 0)
            {
                await Clients.Caller.dialog(new Dialog
                {
                    Header = "Thanks for registering",
                    Question = @"Please wait until the admin team approves your account", Answer1 = "Ok"
                });
                return;
            }

            MainManager.GetRoomManager().AddMember(user.ID, Context.ConnectionId);
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

            await Groups.AddToGroupAsync(Context.ConnectionId, user.ID.ToString());
            await Groups.AddToGroupAsync(Context.ConnectionId, General.LoggedInGroupName);
            if (user.ApiKey != null)
                await Groups.AddToGroupAsync(Context.ConnectionId, user.ApiKey);
            if (user.userprivileges >= UserPrivileges.Elevated)
                await Groups.AddToGroupAsync(Context.ConnectionId, General.AdminGroupName);
            result = user;
        }

        await Clients.Caller.userlogin(result.ToDTO());
    }


    [ErrorHandling]
    public async Task RegisterRequest(DbUser requestUser)
    {
        if (_postgres.Users?.Any(x => x.username == requestUser.username) == false)
        {
            var userNameRegexString = @"^\w(?:\w|.-){2,31}$";
            var userNameRegex = new Regex(userNameRegexString);
            if (!userNameRegex.IsMatch(requestUser.username))
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                    { Header = "Error", Question = $"Username {requestUser.username} not allowed", Answer1 = "Ok" });
                return;
            }

            // Make sure the password hash length is 128 characters (SHA512 length)
            if (requestUser.password.Length != 128)
            {
                await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                    { Header = "Error", Question = "Invalid password hash", Answer1 = "Ok" });
                return;
            }

            requestUser.StreamToken = requestUser.GenerateStreamToken().Token;
            await _postgres.Users.AddAsync(requestUser);
            await _postgres.SaveChangesAsync();

            await Clients.Group(General.LoggedInGroupName).getusers(_postgres.Users.Select(x => x.ToDTO()).ToList());
            await Clients.Caller.userRegister(requestUser.ToDTO());
        }
        else
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "Username already exists", Answer1 = "Ok" });
        }
    }

    [ErrorHandling]
    public async Task GenerateRememberToken(DbUser requestUser, string userInfo)
    {
        try
        {
            if (requestUser == null || string.IsNullOrEmpty(requestUser.password)) return;

            var dbUser = _postgres.Users.FirstOrDefault(x => x.ID == requestUser.ID);
            var token = dbUser?.GenerateToken(userInfo);
            if (dbUser?.RememberTokens.Any(x => x.Token == token?.Token) == true)
                await Clients.Caller.rememberToken(new RememberTokenDTO(token, dbUser.ID));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }

    public async Task ValidateToken(string token, int userID)
    {
        try
        {
            var dbUser = _postgres.Users?.Where(x => x.ID == userID).Include(x => x.RememberTokens).FirstOrDefault();
            if (dbUser == null || dbUser?.RememberTokens.Any(x => x.Token == token) == false)
            {
                await Clients.Caller.userlogin(new DbUser("").ToDTO());
                return;
            }

            if (dbUser.approved <= 0)
            {
                await Clients.Caller.dialog(new Dialog
                {
                    Header = "Thanks for registering",
                    Question = @"Please wait until the admin team approves your account", Answer1 = "Ok"
                });
                return;
            }

            dbUser.RememberTokens = dbUser.RememberTokens.GroupBy(x => x.Token)?.Select(x => x.First()).ToList();
            foreach (var t in dbUser.RememberTokens.ToList())
                if ((DateTime.UtcNow - t.Created).TotalDays > 30)
                {
                    dbUser.RememberTokens.Remove(t);
                    _postgres.RememberTokens.Remove(t);
                }

            if (dbUser.StreamToken == null) dbUser.StreamToken = dbUser.GenerateStreamToken().Token;

            var Token = dbUser.RememberTokens.FirstOrDefault(x => x.Token == token);
            if (Token != null)
            {
                MainManager.GetRoomManager().AddMember(dbUser.ID, Context.ConnectionId);
                await Groups.AddToGroupAsync(Context.ConnectionId, Token.Token);
                await Groups.AddToGroupAsync(Context.ConnectionId, dbUser.ID.ToString());
                await Groups.AddToGroupAsync(Context.ConnectionId, General.LoggedInGroupName);
                if (dbUser.ApiKey != null)
                    await Groups.AddToGroupAsync(Context.ConnectionId, dbUser.ApiKey);
                if (dbUser.userprivileges >= UserPrivileges.Elevated)
                    await Groups.AddToGroupAsync(Context.ConnectionId, General.AdminGroupName);
                await Clients.Caller.userlogin(dbUser.ToDTO());
                Token.Created = DateTime.UtcNow;
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


    [ErrorHandling]
    public async Task ChangeUser(DbUser user, string password)
    {
        var changeUser = _postgres.Users.FirstOrDefault(x => x.ID == user.ID && password == x.password);
        if (changeUser != null)
        {
            var endMsg = "";
            if (changeUser.username != user.username)
            {
                if (changeUser.username.Length < 2 || changeUser.username.Length > 20)
                {
                    await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                    {
                        Header = "Error", Question = "Username must be between 2 and 20 characters", Answer1 = "Ok"
                    });
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
            await Clients.Caller.dialog(new Dialog { Header = "Success", Question = endMsg, Answer1 = "Ok" });
            var users = _postgres.Users.ToList();
            await Clients.All.getusers(users?.Select(x => x.ToDTO()).ToList());
        }
        else
        {
            await Clients.Caller.dialog(new Dialog(AlertType.Danger)
                { Header = "Error", Question = "You password was not correct", Answer1 = "Ok" });
        }
    }
}
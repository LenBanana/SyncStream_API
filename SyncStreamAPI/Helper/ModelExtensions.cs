using System;
using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper;

public static class ModelExtensions
{
    public static DbRememberToken GenerateToken(this DbUser user, string userInfo)
    {
        var tokenString = user.ID + user.username + userInfo + user.usersalt;
        var shaToken = Encryption.Sha256(tokenString);
        var token = new DbRememberToken
        {
            Token = shaToken
        };
        return token;
    }

    public static DbRememberToken GenerateStreamToken(this DbUser user)
    {
        var uid = Guid.NewGuid().ToString();
        var md5Uid = Encryption.CreateMD5(uid);
        var token = new DbRememberToken
        {
            Token = md5Uid
        };
        return token;
    }

    public static UserDTO ToDTO(this DbUser user)
    {
        return new UserDTO(user);
    }
}
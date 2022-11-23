using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;
using System;

namespace SyncStreamAPI.Helper
{
    public static class ModelExtensions
    {
        public static DbRememberToken GenerateToken(this DbUser user, string userInfo)
        {
            string tokenString = user.ID + user.username + userInfo + user.usersalt;
            string shaToken = Encryption.Sha256(tokenString);
            DbRememberToken token = new DbRememberToken();
            token.Token = shaToken;
            return token;
        }

        public static DbRememberToken GenerateStreamToken(this DbUser user)
        {
            string uid = Guid.NewGuid().ToString();
            var md5Uid = Encryption.CreateMD5(uid);
            DbRememberToken token = new DbRememberToken();
            token.Token = md5Uid;
            return token;
        }

        public static UserDTO ToDTO(this DbUser user)
        {
            return new UserDTO(user);
        }
    }
}

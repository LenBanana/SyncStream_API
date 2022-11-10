using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;

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

        public static UserDTO ToDTO(this DbUser user)
        {
            return new UserDTO(user);
        }
    }
}

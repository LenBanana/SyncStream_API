using SyncStreamAPI.Models;
using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Helper
{
    public static class ModelExtensions
    {
        public static RememberToken GenerateToken(this User user, string userInfo)
        {
            string tokenString = user.ID + user.username + userInfo + user.usersalt;
            string shaToken = Encryption.Sha256(tokenString);
            RememberToken token = new RememberToken();
            token.Token = shaToken;
            return token;
        }

        public static UserDTO ToDTO(this User user)
        {
            return new UserDTO(user);
        }
    }
}

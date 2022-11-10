using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models
{
    public class RememberTokenDTO : DbRememberToken
    {
        public int userID { get; set; }

        public RememberTokenDTO(DbRememberToken rememberToken, int userId)
        {
            userID = userId;
            ID = rememberToken.ID;
            Token = rememberToken.Token;
        }
    }
}

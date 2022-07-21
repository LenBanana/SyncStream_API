using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models
{
    public class RememberTokenDTO : RememberToken
    {
        public int userID { get; set; }

        public RememberTokenDTO(RememberToken rememberToken, int userId)
        {
            userID = userId;
            ID = rememberToken.ID;
            Token = rememberToken.Token;
        }
    }
}

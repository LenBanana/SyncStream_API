using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models
{
    public class UserDTO : DbUser
    {
        public UserDTO()
        {

        }

        public UserDTO(DbUser user)
        {
            this.username = user.username;
            this.userprivileges = user.userprivileges;
            this.approved = user.approved;
            this.ID = user.ID;
            this.password = null;
            this.usersalt = null;
            this.RememberTokens = null;
        }
    }
}

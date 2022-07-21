using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models
{
    public class UserDTO : User
    {
        public UserDTO()
        {

        }

        public UserDTO(User user)
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

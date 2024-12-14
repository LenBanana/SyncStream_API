using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Models;

public class UserDTO : DbUser
{
    public UserDTO()
    {
    }

    public UserDTO(DbUser user)
    {
        username = user.username;
        userprivileges = user.userprivileges;
        approved = user.approved;
        ID = user.ID;
        password = null;
        usersalt = null;
        RememberTokens = null;
        StreamToken = user.StreamToken;
        ApiKey = user.ApiKey;
    }
}
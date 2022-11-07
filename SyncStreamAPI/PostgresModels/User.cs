using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.PostgresModels
{
    public class User
    {
        public int ID { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public int approved { get; set; }
        public UserPrivileges userprivileges { get; set; }
        public string usersalt { get; set; }
        public List<RememberToken> RememberTokens { get; set; }
        public List<DbFile> Files { get; set; }
        public User()
        {
            username = null;
            RememberTokens = new List<RememberToken>();
            Files = new List<DbFile>();
            usersalt = Guid.NewGuid().ToString();
        }
        public User(string user)
        {
            username = user;
            approved = -1;
            RememberTokens = new List<RememberToken>();
            Files = new List<DbFile>();
        }
    }

    public enum UserPrivileges
    {
        NotApproved = 0,
        Approved,
        Moderator,
        Administrator,
        Elevated
    }
}

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SyncStreamAPI.PostgresModels
{
    public class DbUser
    {
        public int ID { get; set; }
        public string username { get; set; }
        public string password { get; set; }
        public int approved { get; set; }
        public UserPrivileges userprivileges { get; set; }
        public string usersalt { get; set; }
        public List<DbRememberToken> RememberTokens { get; set; }
        public List<DbFile> Files { get; set; }
        public DbUser()
        {
            username = null;
            RememberTokens = new List<DbRememberToken>();
            Files = new List<DbFile>();
            usersalt = Guid.NewGuid().ToString();
        }
        public DbUser(string user)
        {
            username = user;
            approved = -1;
            RememberTokens = new List<DbRememberToken>();
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
using System;
using System.Collections.Generic;
using SyncStreamAPI.Helper;

namespace SyncStreamAPI.PostgresModels;

public class DbUser
{
    public DbUser()
    {
        username = null;
        RememberTokens = new List<DbRememberToken>();
        StartedConversations = new List<DbConversation>();
        ReceivedConversations = new List<DbConversation>();
        Files = new List<DbFile>();
        StreamToken = RandomKeys.Readable();
    }

    public DbUser(string user)
    {
        username = user;
        approved = -1;
        RememberTokens = new List<DbRememberToken>();
        StartedConversations = new List<DbConversation>();
        ReceivedConversations = new List<DbConversation>();
        Files = new List<DbFile>();
    }

    public int ID { get; set; }
    public string username { get; set; }
    public string password { get; set; }
    public int approved { get; set; }
    public UserPrivileges userprivileges { get; set; }
    public List<DbRememberToken> RememberTokens { get; set; }
    public List<DbConversation> StartedConversations { get; set; }
    public List<DbConversation> ReceivedConversations { get; set; }
    public string StreamToken { get; set; }
    /// <summary>
    ///     Key for programmatic callers, null until one is generated.
    /// </summary>
    /// <remarks>
    ///     Nullable because most accounts never have one. It used to be a non-null column that nothing set on
    ///     the server, and only worked because the client happened to post an empty string inside the entity
    ///     it was allowed to bind. Creating a user server-side violated the constraint.
    /// </remarks>
    public string? ApiKey { get; set; }
    public List<DbFile> Files { get; set; }
}

public enum UserPrivileges
{
    NotApproved = 0,
    Approved,
    Moderator,
    Administrator,
    Elevated
}
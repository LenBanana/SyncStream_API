using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Wire.Users;

/// <summary>
///     The signed-in user's own profile. Carries the stream token and API key because the settings
///     UI shows the user their own keys, so this type must only ever be sent to the user it describes
///     (<c>Clients.Caller</c> or a user-scoped send). Never use it for a list or a broadcast.
/// </summary>
public class SelfProfileDto
{
    public SelfProfileDto()
    {
    }

    public SelfProfileDto(DbUser user)
    {
        ID = user.ID;
        username = user.username;
        approved = user.approved;
        userprivileges = user.userprivileges;
        StreamToken = user.StreamToken;
        ApiKey = user.ApiKey;
    }

    public int ID { get; set; }
    public string? username { get; set; }
    public int approved { get; set; }
    public UserPrivileges userprivileges { get; set; }
    public string? StreamToken { get; set; }
    public string? ApiKey { get; set; }
}

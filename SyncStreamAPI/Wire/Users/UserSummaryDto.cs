using SyncStreamAPI.PostgresModels;

namespace SyncStreamAPI.Wire.Users;

/// <summary>
///     The only user shape that may be sent to more than one recipient. Carries no credentials:
///     no password, no salt, no remember tokens, no stream token, no API key.
///     Use <see cref="SelfProfileDto" /> when the payload goes to the user it describes.
/// </summary>
public class UserSummaryDto
{
    public UserSummaryDto()
    {
    }

    public UserSummaryDto(DbUser user)
    {
        ID = user.ID;
        username = user.username;
        approved = user.approved;
        userprivileges = user.userprivileges;
    }

    public int ID { get; set; }
    public string? username { get; set; }
    public int approved { get; set; }
    public UserPrivileges userprivileges { get; set; }
}

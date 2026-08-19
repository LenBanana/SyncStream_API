namespace SyncStreamAPI.Enums;

public enum UserUpdate
{
    /// <summary>The chosen display name was rejected: malformed, or reserved by a registered account.</summary>
    NameNotAllowed = -4,
    RoomNotExist = -3,
    Banned = -2,
    WrongPassword = -1,
    Success = 1
}

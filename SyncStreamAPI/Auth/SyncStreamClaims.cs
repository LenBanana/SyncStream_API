namespace SyncStreamAPI.Auth;

public static class SyncStreamClaims
{
    /// <summary>The authenticated account's database id.</summary>
    public const string UserId = "syncstream:userid";

    /// <summary>The authenticated account's privilege level, as the numeric enum value.</summary>
    public const string Privilege = "syncstream:privilege";

    /// <summary>Whether the account has been approved by an admin.</summary>
    public const string Approved = "syncstream:approved";
}

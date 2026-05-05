namespace SyncStreamAPI.Enums;

public enum PlayerType
{
    Nothing,
    YouTube,
    Twitch,
    Vimeo,
    External,
    Live,
    WhiteBoard,
    Blackjack,
    Chess,
    WebRtc,
    /// <summary>A room member is streaming a local file via WebRTC/SFU to the room.</summary>
    FileShare
}
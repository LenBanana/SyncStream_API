using System.Collections.Generic;

namespace SyncStreamAPI.Models.RTMP;

public class RtmpPlaybackPreferences
{
    public const string SubtitleModeAuto = "auto";
    public const string SubtitleModeAlways = "always";
    public const string SubtitleModeNever = "never";

    public const string JapaneseLanguage = "japanese";
    public const string EnglishLanguage = "english";
    public const string GermanLanguage = "german";
    public const string OtherLanguage = "other";

    public string SubtitleMode { get; set; } = SubtitleModeAuto;

    public int? SelectedAudioOrdinal { get; set; }

    public int? SelectedSubtitleOrdinal { get; set; }

    public string? SelectedSubtitleCodecName { get; set; }

    public List<string> AudioLanguagePriority { get; set; } =
        new() { JapaneseLanguage, EnglishLanguage, GermanLanguage, OtherLanguage };
}
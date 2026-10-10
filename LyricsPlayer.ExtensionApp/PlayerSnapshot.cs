using LyricsPlayer.ExtensionApp.Lyrics;
using LyricsPlayer.ExtensionApp.Media;

namespace LyricsPlayer.ExtensionApp;

public enum LyricsStatus { Idle, Searching, Found, NotFound }

/// <summary>Everything a view needs for one frame.</summary>
public sealed record PlayerSnapshot(
    TrackInfo? Track,
    bool IsPlaying,
    bool CanSeek,
    TimeSpan Position,
    LyricsDocument Lyrics,
    LyricsStatus Status,
    int LineIndex,
    double LineProgress);

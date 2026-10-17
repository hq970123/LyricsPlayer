using System.Collections.Concurrent;

namespace LyricsPlayer.ExtensionApp.Media;

/// <summary>Turns a media session's AppUserModelId into a name people recognise.</summary>
public static class AppNames
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Matched against the whole id first, then as a prefix / substring (case-insensitive).
    private static readonly (string Key, string Name)[] Known =
    {
        ("cloudmusic", "网易云音乐"),
        ("NetEase", "网易云音乐"),
        ("QQMusic", "QQ 音乐"),
        ("Tencent.QQMusic", "QQ 音乐"),
        ("KuGou", "酷狗音乐"),
        ("kwmusic", "酷我音乐"),
        ("KwMusic", "酷我音乐"),
        ("lx-music", "洛雪音乐"),
        ("Spotify", "Spotify"),
        ("AppleInc.AppleMusic", "Apple Music"),
        ("AppleMusic", "Apple Music"),
        ("iTunes", "iTunes"),
        ("Microsoft.ZuneMusic", "媒体播放器"),
        ("Microsoft.ZuneVideo", "电影和电视"),
        ("foobar2000", "foobar2000"),
        ("MusicBee", "MusicBee"),
        ("AIMP", "AIMP"),
        ("PotPlayer", "PotPlayer"),
        ("vlc", "VLC"),
        ("YesPlayMusic", "YesPlayMusic"),
        ("Chrome", "Chrome 浏览器"),
        ("MSEdge", "Edge 浏览器"),
        ("308046B0AF4A39CB", "Firefox 浏览器"),
        ("Firefox", "Firefox 浏览器"),
        ("Opera", "Opera 浏览器"),
        ("Brave", "Brave 浏览器"),
        ("Bilibili", "哔哩哔哩"),
    };

    /// <summary>Our own name for a well-known player or browser, or null.</summary>
    public static string? TryKnown(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        foreach (var (key, name) in Known)
            if (id.Contains(key, StringComparison.OrdinalIgnoreCase)) return name;
        return null;
    }

    // SMTC ids that don't contain the exe name.
    private static readonly (string Aumid, string Exe)[] Aliases =
    {
        ("308046B0AF4A39CB", "firefox.exe"),
        ("6F193CCC56814779", "firefox.exe"), // Firefox Nightly / Developer Edition
        ("MSEdge", "msedge.exe"),
        ("Chrome", "chrome.exe"),
    };

    /// <summary>
    /// Does an SMTC session (by its AppUserModelId) belong to the app saved as <paramref name="key"/>
    /// (exe name in lower case, or an AUMID for apps that only showed up as a media session)?
    /// </summary>
    public static bool Matches(string key, string? savedAumid, string? sessionAumid)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(sessionAumid)) return false;
        var id = sessionAumid.Trim();
        if (string.Equals(id, key, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrWhiteSpace(savedAumid) && string.Equals(id, savedAumid, StringComparison.OrdinalIgnoreCase)) return true;

        if (!key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = key[..^4];
        // "cloudmusic.exe", "C:\...\cloudmusic.exe", "cloudmusic"
        if (id.EndsWith("\\" + key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(id, stem, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var (aumid, exe) in Aliases)
            if (string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase))
                return string.Equals(exe, key, StringComparison.OrdinalIgnoreCase);
        // Explicit AUMIDs like "Tencent.QQMusic" or "SpotifyAB.SpotifyMusic_xxx!Spotify".
        return stem.Length >= 5 && id.Contains(stem, StringComparison.OrdinalIgnoreCase);
    }

    public static string Friendly(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "未知应用";
        return Cache.GetOrAdd(appId, Resolve);
    }

    private static string Resolve(string id)
    {
        if (TryKnown(id) is { } known) return known;

        // Packaged apps: ask Windows for the display name.
        try
        {
            var info = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(id);
            var display = info?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(display)) return display;
        }
        catch { /* not available for every app */ }

        // "Vendor.App_hash!App" -> "App", "C:\\x\\player.exe" / "player.exe" -> "player"
        var name = id;
        var bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1) name = name[(bang + 1)..];
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0 && slash < name.Length - 1) name = name[(slash + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Length > 0 ? name : id;
    }
}

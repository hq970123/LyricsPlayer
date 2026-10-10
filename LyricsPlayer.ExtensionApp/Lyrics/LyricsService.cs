using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LyricsPlayer.ExtensionApp.Lyrics;

public sealed record TrackQuery(string Title, string Artist, string Album, TimeSpan Duration);

/// <summary>
/// Finds synced lyrics for a track. Sources: NetEase Cloud Music (best for Chinese
/// songs) and LRCLIB (open database, best for Western songs). Results, including
/// "not found", are cached on disk in the widget's data directory.
/// </summary>
public sealed partial class LyricsService
{
    // HttpClient is stateless here, so one per process is fine (and recommended).
    private static readonly HttpClient Http = CreateClient();

    private readonly string _cacheDir;
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(1);

    public LyricsService(string dataDirectory)
    {
        _cacheDir = Path.Combine(dataDirectory, "lyrics-cache");
        Directory.CreateDirectory(_cacheDir);
    }

    public bool UseNetease { get; set; } = true;
    public bool UseLrclib { get; set; } = true;

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "LyricsPlayer-WidBar/1.0 (+https://github.com/andelby/widbar-widget-template)");
        return client;
    }

    public async Task<LyricsDocument> GetAsync(TrackQuery query, CancellationToken ct)
    {
        var cacheFile = Path.Combine(_cacheDir, CacheKey(query) + ".json");
        var cached = ReadCache(cacheFile);
        if (cached is not null) return cached;

        var providers = new List<Func<TrackQuery, CancellationToken, Task<CachedLyrics?>>>();
        bool cjk = HasCjk(query.Title) || HasCjk(query.Artist);
        if (cjk)
        {
            if (UseNetease) providers.Add(FromNeteaseAsync);
            if (UseLrclib) providers.Add(FromLrclibAsync);
        }
        else
        {
            if (UseLrclib) providers.Add(FromLrclibAsync);
            if (UseNetease) providers.Add(FromNeteaseAsync);
        }

        CachedLyrics? plainFallback = null;
        bool anyProviderFailed = false;
        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var found = await provider(query, ct);
                if (found is null) continue;
                var doc = LrcParser.Parse(found.Lrc, found.Translation, found.Source);
                if (doc.IsSynced)
                {
                    WriteCache(cacheFile, found);
                    return doc;
                }
                plainFallback ??= found;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                anyProviderFailed = true;
                Debug.WriteLine($"Lyrics provider failed: {ex.Message}");
            }
        }

        if (plainFallback is not null)
        {
            WriteCache(cacheFile, plainFallback);
            return LrcParser.Parse(plainFallback.Lrc, null, plainFallback.Source);
        }

        // Only remember "not found" when every source actually answered.
        if (!anyProviderFailed && providers.Count > 0)
        {
            WriteCache(cacheFile, new CachedLyrics("", null, "none", DateTimeOffset.UtcNow));
        }
        return LyricsDocument.Empty;
    }

    // ---------------- LRCLIB ----------------

    private static async Task<CachedLyrics?> FromLrclibAsync(TrackQuery q, CancellationToken ct)
    {
        // 1. Exact lookup (fast, needs duration within ±2 s on their side).
        if (q.Duration > TimeSpan.Zero && q.Artist.Length > 0)
        {
            var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(q.Title)
                      + "&artist_name=" + Uri.EscapeDataString(q.Artist)
                      + (q.Album.Length > 0 ? "&album_name=" + Uri.EscapeDataString(q.Album) : "")
                      + "&duration=" + (int)Math.Round(q.Duration.TotalSeconds);
            using var resp = await Http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var node = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
                var hit = LrclibToLyrics(node);
                if (hit is not null) return hit;
            }
            else if (resp.StatusCode != HttpStatusCode.NotFound)
            {
                resp.EnsureSuccessStatusCode(); // 5xx -> treated as provider failure
            }
        }

        // 2. Search and pick the best match ourselves.
        var searchUrl = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(CleanTitle(q.Title))
                        + (q.Artist.Length > 0 ? "&artist_name=" + Uri.EscapeDataString(q.Artist) : "");
        using var sresp = await Http.GetAsync(searchUrl, ct);
        sresp.EnsureSuccessStatusCode();
        if (JsonNode.Parse(await sresp.Content.ReadAsStringAsync(ct)) is not JsonArray arr) return null;

        JsonNode? best = null;
        double bestScore = double.MinValue;
        foreach (var item in arr)
        {
            if (item is null) continue;
            var score = Score(q,
                item["trackName"]?.GetValue<string>() ?? "",
                item["artistName"]?.GetValue<string>() ?? "",
                TimeSpan.FromSeconds(item["duration"]?.GetValue<double>() ?? 0));
            if (!string.IsNullOrEmpty(item["syncedLyrics"]?.GetValue<string>())) score += 2;
            if (score > bestScore) { bestScore = score; best = item; }
        }
        return bestScore >= 3 ? LrclibToLyrics(best) : null;
    }

    private static CachedLyrics? LrclibToLyrics(JsonNode? node)
    {
        if (node is null) return null;
        if (node["instrumental"]?.GetValue<bool>() == true)
            return new CachedLyrics("[00:00.00]♪ 纯音乐，请欣赏 ♪", null, "LRCLIB", DateTimeOffset.UtcNow);
        var synced = node["syncedLyrics"]?.GetValue<string>();
        var plain = node["plainLyrics"]?.GetValue<string>();
        var text = !string.IsNullOrWhiteSpace(synced) ? synced : plain;
        return string.IsNullOrWhiteSpace(text) ? null : new CachedLyrics(text!, null, "LRCLIB", DateTimeOffset.UtcNow);
    }

    // ---------------- NetEase Cloud Music ----------------

    private static async Task<CachedLyrics?> FromNeteaseAsync(TrackQuery q, CancellationToken ct)
    {
        var keywords = (CleanTitle(q.Title) + " " + q.Artist).Trim();
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://music.163.com/api/cloudsearch/pc?type=1&limit=10&offset=0&s=" + Uri.EscapeDataString(keywords));
        req.Headers.Referrer = new Uri("https://music.163.com/");
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var root = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (root?["result"]?["songs"] is not JsonArray songs) return null;

        long bestId = 0;
        double bestScore = double.MinValue;
        foreach (var s in songs)
        {
            if (s is null) continue;
            var artists = string.Join(" / ",
                (s["ar"] as JsonArray)?.Select(a => a?["name"]?.GetValue<string>() ?? "") ?? Array.Empty<string>());
            var score = Score(q,
                s["name"]?.GetValue<string>() ?? "",
                artists,
                TimeSpan.FromMilliseconds(s["dt"]?.GetValue<double>() ?? 0));
            if (score > bestScore) { bestScore = score; bestId = s["id"]?.GetValue<long>() ?? 0; }
        }
        if (bestId == 0 || bestScore < 3) return null;

        using var lreq = new HttpRequestMessage(HttpMethod.Get,
            $"https://music.163.com/api/song/lyric?id={bestId}&lv=1&tv=-1");
        lreq.Headers.Referrer = new Uri("https://music.163.com/");
        using var lresp = await Http.SendAsync(lreq, ct);
        lresp.EnsureSuccessStatusCode();
        var lyr = JsonNode.Parse(await lresp.Content.ReadAsStringAsync(ct));
        if (lyr?["pureMusic"]?.GetValue<bool>() == true || lyr?["nolyric"]?.GetValue<bool>() == true)
            return new CachedLyrics("[00:00.00]♪ 纯音乐，请欣赏 ♪", null, "网易云音乐", DateTimeOffset.UtcNow);

        var lrc = lyr?["lrc"]?["lyric"]?.GetValue<string>();
        var tlrc = lyr?["tlyric"]?["lyric"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(lrc) ? null : new CachedLyrics(lrc!, tlrc, "网易云音乐", DateTimeOffset.UtcNow);
    }

    // ---------------- matching helpers ----------------

    /// <summary>Higher is better. ~3+ means title matches; artist and duration add confidence.</summary>
    private static double Score(TrackQuery q, string title, string artist, TimeSpan duration)
    {
        double score = 0;
        var qt = Normalize(CleanTitle(q.Title));
        var ct = Normalize(CleanTitle(title));
        if (qt.Length > 0 && qt == ct) score += 3;
        else if (qt.Length > 0 && ct.Length > 0 && (ct.Contains(qt) || qt.Contains(ct))) score += 1.5;
        else return -10;
        // Same version label too, e.g. both "(Live)" or both "(空灵版)".
        if (Normalize(q.Title) == Normalize(title)) score += 0.5;

        if (q.Artist.Length > 0 && artist.Length > 0)
        {
            var qa = SplitArtists(q.Artist);
            var ca = SplitArtists(artist);
            if (qa.Overlaps(ca)) score += 2;
            else if (qa.Any(a => ca.Any(c => c.Contains(a) || a.Contains(c)))) score += 1;
            else score -= 1;
        }

        if (q.Duration > TimeSpan.Zero && duration > TimeSpan.Zero)
        {
            var diff = Math.Abs((q.Duration - duration).TotalSeconds);
            if (diff <= 2) score += 2;
            else if (diff <= 5) score += 1;
            else if (diff > 15) score -= 3; // different version (live, sped up, cover)
        }
        return score;
    }

    private static HashSet<string> SplitArtists(string s) =>
        ArtistSplit().Split(s).Select(Normalize).Where(a => a.Length > 0).ToHashSet();

    [GeneratedRegex(@"\s*(?:,|，|、|/|&|;|\bfeat\.?|\bft\.?|\bx\b)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistSplit();

    [GeneratedRegex(@"\s*[\(\[（【].*?[\)\]）】]\s*")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\s+-\s+.*(remaster|live|version|edit|mix|mono|stereo).*$", RegexOptions.IgnoreCase)]
    private static partial Regex DashSuffix();

    [GeneratedRegex(@"[\s\p{P}\p{S}]+")]
    private static partial Regex NonWord();

    public static string CleanTitle(string title)
    {
        var t = DashSuffix().Replace(title, "");
        var stripped = Brackets().Replace(t, " ").Trim();
        return stripped.Length > 0 ? stripped : t.Trim();
    }

    private static string Normalize(string s) => NonWord().Replace(s.ToLowerInvariant(), "");

    private static bool HasCjk(string s) =>
        s.Any(c => c is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\uac00' and <= '\ud7af');

    // ---------------- cache ----------------

    private sealed record CachedLyrics(string Lrc, string? Translation, string Source, DateTimeOffset SavedAt);

    private static string CacheKey(TrackQuery q)
    {
        var raw = $"{q.Title}\u001f{q.Artist}\u001f{(int)(q.Duration.TotalSeconds / 5)}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw)))[..20];
    }

    private static LyricsDocument? ReadCache(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var c = JsonSerializer.Deserialize<CachedLyrics>(File.ReadAllText(file));
            if (c is null) return null;
            if (c.Source == "none")
                return DateTimeOffset.UtcNow - c.SavedAt < NotFoundTtl ? LyricsDocument.Empty : null;
            return LrcParser.Parse(c.Lrc, c.Translation, c.Source);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(string file, CachedLyrics c)
    {
        try { File.WriteAllText(file, JsonSerializer.Serialize(c)); }
        catch (Exception ex) { Debug.WriteLine($"Lyrics cache write failed: {ex.Message}"); }
    }

    /// <summary>Removes the cached entry so the next lookup goes back to the network.</summary>
    public void Invalidate(TrackQuery q)
    {
        try { File.Delete(Path.Combine(_cacheDir, CacheKey(q) + ".json")); } catch { }
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricsPlayer.ExtensionApp.Lyrics;

public sealed record LyricLine(TimeSpan Time, string Text, string? Translation);

/// <summary>A parsed lyric document. Synced documents have meaningful times.</summary>
public sealed class LyricsDocument
{
    public static readonly LyricsDocument Empty = new(Array.Empty<LyricLine>(), false, "");

    public LyricsDocument(IReadOnlyList<LyricLine> lines, bool isSynced, string source)
    {
        Lines = lines;
        IsSynced = isSynced;
        Source = source;
    }

    public IReadOnlyList<LyricLine> Lines { get; }
    public bool IsSynced { get; }
    public string Source { get; }
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Index of the line playing at <paramref name="position"/>, or -1 before the first line.</summary>
    public int IndexAt(TimeSpan position)
    {
        if (!IsSynced || Lines.Count == 0) return -1;
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (Lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    /// <summary>0..1 progress through line <paramref name="index"/>, used for the karaoke wipe.</summary>
    public double ProgressAt(int index, TimeSpan position, TimeSpan duration)
    {
        if (index < 0 || index >= Lines.Count) return 0;
        var start = Lines[index].Time;
        var end = index + 1 < Lines.Count
            ? Lines[index + 1].Time
            : (duration > start ? duration : start + TimeSpan.FromSeconds(5));
        // Lines with a long instrumental gap after them should not crawl for 30 s.
        var span = end - start;
        if (span > TimeSpan.FromSeconds(10)) span = TimeSpan.FromSeconds(10);
        if (span <= TimeSpan.Zero) return 1;
        return Math.Clamp((position - start).TotalMilliseconds / span.TotalMilliseconds, 0, 1);
    }
}

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"^\[[a-zA-Z]+:.*\]$")]
    private static partial Regex MetaTag();

    public static LyricsDocument Parse(string? lrc, string? translationLrc, string source)
    {
        var main = ParseTimed(lrc);
        if (main.Count == 0)
        {
            // Plain (unsynced) lyrics: keep the text, mark the document unsynced.
            var plain = (lrc ?? "")
                .Replace("\r", "")
                .Split('\n')
                .Select(l => TimeTag().Replace(l, "").Trim())
                .Where(l => l.Length > 0 && !MetaTag().IsMatch(l))
                .Select(l => new LyricLine(TimeSpan.Zero, l, null))
                .ToList();
            return plain.Count == 0 ? LyricsDocument.Empty : new LyricsDocument(plain, false, source);
        }

        var translations = ParseTimed(translationLrc)
            .Where(t => t.Text.Length > 0)
            .GroupBy(t => Key(t.Time))
            .ToDictionary(g => g.Key, g => g.First().Text);

        var lines = main
            .Select(l => l with
            {
                Translation = translations.TryGetValue(Key(l.Time), out var tr) && tr != l.Text ? tr : null,
            })
            .ToList();

        // Drop empty lines at the very start, keep empty lines in the middle (they mark breaks).
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0].Text)) lines.RemoveAt(0);
        return lines.Count == 0 ? LyricsDocument.Empty : new LyricsDocument(lines, true, source);
    }

    // Timestamps are matched at 10 ms resolution; translation files round differently.
    private static long Key(TimeSpan t) => (long)Math.Round(t.TotalMilliseconds / 10.0);

    private static List<LyricLine> ParseTimed(string? lrc)
    {
        var result = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrc)) return result;

        var offset = TimeSpan.Zero;
        foreach (var raw in lrc.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var off = OffsetTag().Match(line);
            if (off.Success)
            {
                // Positive offset means lyrics should appear earlier.
                offset = -TimeSpan.FromMilliseconds(int.Parse(off.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var matches = TimeTag().Matches(line);
            if (matches.Count == 0) continue;

            var text = TimeTag().Replace(line, "").Trim();
            foreach (Match m in matches)
            {
                int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                int sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                int ms = 0;
                if (m.Groups[3].Success)
                {
                    var frac = m.Groups[3].Value;
                    ms = int.Parse(frac, CultureInfo.InvariantCulture) * (frac.Length switch { 1 => 100, 2 => 10, _ => 1 });
                }
                var time = new TimeSpan(0, 0, min, sec, ms) + offset;
                if (time < TimeSpan.Zero) time = TimeSpan.Zero;
                result.Add(new LyricLine(time, text, null));
            }
        }

        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }
}

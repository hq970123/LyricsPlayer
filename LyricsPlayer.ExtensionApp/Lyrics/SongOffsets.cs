using System.Diagnostics;
using System.Text.Json;

namespace LyricsPlayer.ExtensionApp.Lyrics;

/// <summary>
/// Per-song lyric timing tweaks made from the flyout's -/+ buttons, kept in
/// song-offsets.json in the widget's data folder. Key is "title \u001f artist".
/// </summary>
public sealed class SongOffsets
{
    private readonly string _path;
    private readonly Dictionary<string, int> _map;

    public SongOffsets(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "song-offsets.json");
        try
        {
            _map = File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"song offsets unreadable: {ex.Message}");
            _map = new();
        }
    }

    public int Get(string key) => _map.TryGetValue(key, out var v) ? v : 0;

    public void Set(string key, int ms)
    {
        if (ms == 0) _map.Remove(key);
        else _map[key] = ms;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_map));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"song offsets not saved: {ex.Message}");
        }
    }
}

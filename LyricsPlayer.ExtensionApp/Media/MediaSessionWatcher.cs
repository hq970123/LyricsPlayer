using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Windows.Media.Control;

namespace LyricsPlayer.ExtensionApp.Media;

/// <summary>A snapshot of the track the system is playing.</summary>
public sealed record TrackInfo(
    string Title,
    string Artist,
    string Album,
    TimeSpan Duration,
    string SourceAppId,
    byte[]? Thumbnail)
{
    public string Key => $"{Title}\u001f{Artist}\u001f{SourceAppId}";
}

/// <summary>A media app that has (or had) a system media session.</summary>
public sealed record MediaAppInfo(string AppId, string Name, bool IsPlaying);

/// <summary>
/// Watches the Windows media session (SMTC), which every mainstream player feeds:
/// NetEase Cloud Music, QQ Music, Spotify, Apple Music, foobar2000, browsers...
/// All events are raised on the UI thread.
/// </summary>
public sealed class MediaSessionWatcher : IDisposable
{
    private readonly DispatcherQueue _ui;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private bool _disposed;

    // Optional whitelist (exe key or AUMID, plus the package AUMID). Null = follow whatever Windows plays.
    private List<(string Key, string? Aumid)>? _allowed;
    // Every session we listen to for play/pause, so a whitelisted app that starts playing
    // in the background takes over even when Windows keeps another app "current".
    private readonly List<GlobalSystemMediaTransportControlsSession> _watched = new();

    // Timeline state for position interpolation.
    private TimeSpan _reportedPosition;
    private DateTimeOffset _reportedAt;       // when that position was true
    private bool _hasTimeline;                // false for players that never report a position
    private readonly Stopwatch _selfClock = new(); // fallback clock for those players
    private TimeSpan _selfClockBase;

    public MediaSessionWatcher(DispatcherQueue uiQueue) => _ui = uiQueue;

    public TrackInfo? Track { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool HasSession => _session is not null && Track is not null;
    public bool CanSeek { get; private set; }
    public bool CanShuffle { get; private set; }
    public bool IsShuffleOn { get; private set; }
    public bool CanRepeat { get; private set; }
    public Windows.Media.MediaPlaybackAutoRepeatMode RepeatMode { get; private set; }

    /// <summary>Title, artist or cover changed.</summary>
    public event EventHandler? TrackChanged;
    /// <summary>Play/pause, timeline or session availability changed.</summary>
    public event EventHandler? StateChanged;
    /// <summary>A media app opened or closed its session (used by the settings page list).</summary>
    public event EventHandler? SessionsListChanged;

    /// <summary>True when only whitelisted apps are followed.</summary>
    public bool IsFiltering => _allowed is { Count: > 0 };

    /// <summary>
    /// Follow only these apps (see AppNames.Matches for how a media session is tied to an app).
    /// Null or empty goes back to automatic. Takes effect immediately.
    /// </summary>
    public void SetFilter(IEnumerable<(string Key, string? Aumid)>? apps)
    {
        var next = apps?.Where(a => !string.IsNullOrWhiteSpace(a.Key)).ToList();
        if (next is { Count: 0 }) next = null;
        static string Sig(List<(string Key, string? Aumid)>? l) =>
            l is null ? "" : string.Join("|", l.Select(a => $"{a.Key}>{a.Aumid}".ToLowerInvariant()).OrderBy(x => x));
        if (Sig(next) == Sig(_allowed)) return;
        _allowed = next;
        if (_manager is not null) _ = RepickAsync();
    }

    private bool IsAllowed(GlobalSystemMediaTransportControlsSession s)
    {
        if (_allowed is null) return true;
        string id;
        try { id = s.SourceAppUserModelId ?? ""; }
        catch { return false; }
        foreach (var (key, aumid) in _allowed)
            if (AppNames.Matches(key, aumid, id)) return true;
        return false;
    }

    /// <summary>The media apps that have a session right now.</summary>
    public IReadOnlyList<MediaAppInfo> GetSessionApps()
    {
        var list = new List<MediaAppInfo>();
        if (_manager is null) return list;
        try
        {
            foreach (var s in _manager.GetSessions())
            {
                string id;
                bool playing;
                try
                {
                    id = s.SourceAppUserModelId ?? "";
                    playing = s.GetPlaybackInfo()?.PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                }
                catch { continue; }
                if (id.Length == 0) continue;
                var existing = list.FindIndex(a => string.Equals(a.AppId, id, StringComparison.OrdinalIgnoreCase));
                if (existing >= 0)
                {
                    if (playing) list[existing] = list[existing] with { IsPlaying = true };
                    continue;
                }
                list.Add(new MediaAppInfo(id, AppNames.Friendly(id), playing));
            }
        }
        catch { /* sessions can vanish mid-enumeration */ }
        return list;
    }

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnSessionsChanged;
        WatchAllSessions();
        await AttachAsync(PickSession());
    }

    private static bool IsPlayingNow(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            return s.GetPlaybackInfo()?.PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { return false; }
    }

    /// <summary>
    /// Among the allowed sessions: keep the current one while it plays, else take one that is
    /// playing, else what Windows calls current, else any allowed (paused) session.
    /// </summary>
    private GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_manager is null) return null;
        var candidates = new List<GlobalSystemMediaTransportControlsSession>();
        try
        {
            foreach (var s in _manager.GetSessions())
                if (IsAllowed(s)) candidates.Add(s);
        }
        catch { /* sessions can vanish mid-enumeration */ }

        if (_session is not null && candidates.Contains(_session) && IsPlayingNow(_session)) return _session;
        foreach (var s in candidates)
            if (IsPlayingNow(s)) return s;

        GlobalSystemMediaTransportControlsSession? current = null;
        try { current = _manager.GetCurrentSession(); } catch { }
        if (current is not null && IsAllowed(current)) return current;
        if (_allowed is null) return current;

        if (_session is not null && candidates.Contains(_session)) return _session;
        return candidates.FirstOrDefault();
    }

    private async Task RepickAsync()
    {
        var next = PickSession();
        if (!ReferenceEquals(next, _session)) await AttachAsync(next);
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) =>
        _ui.TryEnqueue(async () => await RepickAsync());

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) =>
        _ui.TryEnqueue(async () =>
        {
            WatchAllSessions();
            await RepickAsync();
            SessionsListChanged?.Invoke(this, EventArgs.Empty);
        });

    /// <summary>Listen to play/pause on every session, not only the one we show.</summary>
    private void WatchAllSessions()
    {
        foreach (var s in _watched)
        {
            try { s.PlaybackInfoChanged -= OnAnyPlaybackChanged; } catch { }
        }
        _watched.Clear();
        if (_manager is null) return;
        try
        {
            foreach (var s in _manager.GetSessions())
            {
                s.PlaybackInfoChanged += OnAnyPlaybackChanged;
                _watched.Add(s);
            }
        }
        catch { }
    }

    private void OnAnyPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        _ui.TryEnqueue(async () =>
        {
            if (ReferenceEquals(s, _session)) return; // handled by OnPlaybackInfoChanged
            await RepickAsync();
            SessionsListChanged?.Invoke(this, EventArgs.Empty);
        });

    private async Task AttachAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_disposed) return;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
        }

        _session = session;
        if (_session is null)
        {
            Track = null;
            IsPlaying = false;
            TrackChanged?.Invoke(this, EventArgs.Empty);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelineChanged;

        await RefreshMediaPropertiesAsync();
        RefreshPlayback();
        RefreshTimeline();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
        _ui.TryEnqueue(async () => { if (ReferenceEquals(s, _session)) await RefreshMediaPropertiesAsync(); });

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
        _ui.TryEnqueue(() =>
        {
            if (!ReferenceEquals(s, _session)) return;
            RefreshPlayback();
            RefreshTimeline();
            StateChanged?.Invoke(this, EventArgs.Empty);
        });

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
        _ui.TryEnqueue(() =>
        {
            if (!ReferenceEquals(s, _session)) return;
            RefreshTimeline();
            StateChanged?.Invoke(this, EventArgs.Empty);
        });

    private async Task RefreshMediaPropertiesAsync()
    {
        var session = _session;
        if (session is null) return;
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session) || props is null) return;

            var title = props.Title?.Trim() ?? "";
            var artist = props.Artist?.Trim() ?? "";
            // Some players (QQ Music, some web players) put "Title - Artist" in Title.
            if (artist.Length == 0 && title.Contains(" - "))
            {
                var parts = title.Split(" - ", 2);
                title = parts[0].Trim();
                artist = parts[1].Trim();
            }

            if (title.Length == 0)
            {
                if (Track is not null) { Track = null; TrackChanged?.Invoke(this, EventArgs.Empty); }
                return;
            }

            var duration = Track?.Duration ?? TimeSpan.Zero;
            try
            {
                var tl = session.GetTimelineProperties();
                if (tl.EndTime > TimeSpan.Zero) duration = tl.EndTime - tl.StartTime;
            }
            catch { }

            var sameSong = Track is not null && Track.Title == title && Track.Artist == artist
                           && Track.SourceAppId == session.SourceAppUserModelId;

            byte[]? thumb = sameSong ? Track!.Thumbnail : null;
            if (props.Thumbnail is not null)
            {
                try
                {
                    using var stream = await props.Thumbnail.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await stream.AsStreamForRead().CopyToAsync(ms);
                    if (ms.Length > 0) thumb = ms.ToArray();
                }
                catch { }
            }

            if (!sameSong)
            {
                // New song: restart the fallback clock.
                _selfClockBase = TimeSpan.Zero;
                if (IsPlaying) _selfClock.Restart(); else _selfClock.Reset();
            }

            Track = new TrackInfo(title, artist, props.AlbumTitle?.Trim() ?? "", duration,
                session.SourceAppUserModelId ?? "", thumb);
            TrackChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Media properties failed: {ex.Message}");
        }
    }

    private void RefreshPlayback()
    {
        var info = _session?.GetPlaybackInfo();
        var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        CanSeek = info?.Controls?.IsPlaybackPositionEnabled == true;
        CanShuffle = info?.Controls?.IsShuffleEnabled == true;
        IsShuffleOn = info?.IsShuffleActive == true;
        CanRepeat = info?.Controls?.IsRepeatEnabled == true;
        RepeatMode = info?.AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;

        if (playing != IsPlaying)
        {
            if (playing) _selfClock.Start();
            else _selfClock.Stop();
        }
        IsPlaying = playing;
    }

    private void RefreshTimeline()
    {
        if (_session is null) return;
        try
        {
            var tl = _session.GetTimelineProperties();
            _hasTimeline = tl.EndTime > TimeSpan.Zero;
            _reportedPosition = tl.Position;
            // Players that don't stamp LastUpdatedTime report year 1601; use "now" instead.
            _reportedAt = tl.LastUpdatedTime.Year > 2000 && tl.LastUpdatedTime <= DateTimeOffset.Now.AddSeconds(5)
                ? tl.LastUpdatedTime
                : DateTimeOffset.Now;

            if (_hasTimeline && Track is not null)
            {
                var d = tl.EndTime - tl.StartTime;
                if (d > TimeSpan.Zero && d != Track.Duration) Track = Track with { Duration = d };
            }
        }
        catch { }
    }

    /// <summary>Best estimate of the playback position right now.</summary>
    public TimeSpan GetPosition()
    {
        if (!_hasTimeline) return _selfClockBase + _selfClock.Elapsed;

        var pos = _reportedPosition;
        if (IsPlaying)
        {
            var rate = _session?.GetPlaybackInfo()?.PlaybackRate ?? 1.0;
            pos += TimeSpan.FromTicks((long)((DateTimeOffset.Now - _reportedAt).Ticks * rate));
        }
        var d = Track?.Duration ?? TimeSpan.Zero;
        if (d > TimeSpan.Zero && pos > d) pos = d;
        return pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
    }

    public async void TogglePlayPause()
    {
        try { if (_session is not null) await _session.TryTogglePlayPauseAsync(); } catch { }
    }

    public async void Next()
    {
        try { if (_session is not null) await _session.TrySkipNextAsync(); } catch { }
    }

    public async void Previous()
    {
        try { if (_session is not null) await _session.TrySkipPreviousAsync(); } catch { }
    }

    public async void ToggleShuffle()
    {
        try { if (_session is not null) await _session.TryChangeShuffleActiveAsync(!IsShuffleOn); } catch { }
    }

    /// <summary>Off → whole list → one song → off.</summary>
    public async void CycleRepeat()
    {
        var next = RepeatMode switch
        {
            Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
            Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
            _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
        };
        try { if (_session is not null) await _session.TryChangeAutoRepeatModeAsync(next); } catch { }
    }

    public async void Seek(TimeSpan position)
    {
        if (_session is null) return;
        if (!_hasTimeline)
        {
            _selfClockBase = position;
            if (IsPlaying) _selfClock.Restart(); else _selfClock.Reset();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        try
        {
            if (await _session.TryChangePlaybackPositionAsync(position.Ticks))
            {
                _reportedPosition = position;
                _reportedAt = DateTimeOffset.Now;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelineChanged;
            _session = null;
        }
        foreach (var s in _watched)
        {
            try { s.PlaybackInfoChanged -= OnAnyPlaybackChanged; } catch { }
        }
        _watched.Clear();
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnSessionsChanged;
            _manager = null;
        }
    }
}

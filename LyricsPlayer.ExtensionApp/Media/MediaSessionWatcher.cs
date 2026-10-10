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

    /// <summary>Title, artist or cover changed.</summary>
    public event EventHandler? TrackChanged;
    /// <summary>Play/pause, timeline or session availability changed.</summary>
    public event EventHandler? StateChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        _manager.SessionsChanged += OnCurrentSessionChanged;
        await AttachAsync(PickSession());
    }

    /// <summary>The playing session if there is one, otherwise whatever Windows calls current.</summary>
    private GlobalSystemMediaTransportControlsSession? PickSession()
    {
        if (_manager is null) return null;
        try
        {
            foreach (var s in _manager.GetSessions())
            {
                if (s.GetPlaybackInfo()?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return s;
            }
        }
        catch { /* sessions can vanish mid-enumeration */ }
        return _manager.GetCurrentSession();
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
    {
        _ui.TryEnqueue(async () =>
        {
            var next = PickSession();
            if (!ReferenceEquals(next, _session)) await AttachAsync(next);
        });
    }

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
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _manager.SessionsChanged -= OnCurrentSessionChanged;
            _manager = null;
        }
    }
}

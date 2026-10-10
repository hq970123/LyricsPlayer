using System.Diagnostics;
using LyricsPlayer.ExtensionApp.Lyrics;
using LyricsPlayer.ExtensionApp.Media;
using LyricsPlayer.ExtensionApp.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WidBar.SDK;

namespace LyricsPlayer.ExtensionApp;

// Lyrics Player: follows whatever Windows is playing (any app that feeds the system
// media controls), shows the current lyric line on the taskbar with a karaoke wipe,
// and a full scrolling lyric view in the flyout.
public sealed class MainPlugin :
    WidgetPluginBase,
    IConfigurableWidgetPlugin,
    IWidgetFlyoutLifecycle
{
    public override string Id => "com.hq970123.lyricsplayer";
    public override string Name => "Lyrics Player";

    public override int PreviewLogicalWidth => _settings.PreviewWidth;
    public override bool IsPreviewVisible => !_settings.HideWhenIdle || (_media?.HasSession ?? false);
    public override int FlyoutWidth => 380;
    public override int FlyoutHeight => 540;
    public override WidgetFlyoutBackdrop FlyoutBackdrop => WidgetFlyoutBackdrop.Acrylic;

    private WidgetSettings _settings = new();
    private MediaSessionWatcher? _media;
    private LyricsService? _lyricsService;
    private PreviewView? _preview;
    private FlyoutView? _flyout;
    private DispatcherTimer? _timer;
    private bool _flyoutShown;
    private bool _previewOnTop = true;
    private bool _lastVisible = true;

    private LyricsDocument _lyrics = LyricsDocument.Empty;
    private LyricsStatus _status = LyricsStatus.Idle;
    private string? _lyricsForKey;
    private CancellationTokenSource? _lyricsCts;

    public override async Task InitializeAsync(IWidgetContext context)
    {
        _settings = WidgetSettings.FromJson(context.SettingsJson);
        await base.InitializeAsync(context);

        _lyricsService = new LyricsService(context.DataDirectory);
        ApplyProviderSettings();

        var ui = DispatcherQueue.GetForCurrentThread();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _timer.Tick += (_, _) => Render();

        _media = new MediaSessionWatcher(ui);
        _media.TrackChanged += (_, _) => OnTrackChanged();
        _media.StateChanged += (_, _) => { UpdateTimer(); Render(); UpdateVisibility(); };

        _previewOnTop = context.IsPreviewVisible;
        context.PreviewVisibilityChanged += OnPreviewVisibilityChanged;

        try
        {
            await _media.StartAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not start media session watcher: {ex}");
        }
        _lastVisible = IsPreviewVisible;
    }

    public override UIElement? CreatePreviewContent()
    {
        if (_media is null) return null;
        _preview = new PreviewView(_media);
        _preview.ApplySettings(_settings);
        _preview.SetActive(_previewOnTop);
        Render();
        return _preview;
    }

    public override UIElement? CreateFlyoutContent()
    {
        if (_media is null) return null;
        _flyout = new FlyoutView(_media, RefetchLyrics);
        Render();
        return _flyout;
    }

    public UIElement? CreateSettingsContent(IWidgetSettingsContext context) =>
        new SettingsView(context, _media, LiveSnapshot, () => _preview?.ActualWidth ?? 0);

    /// <summary>What the settings page's sample preview shows: the real song, or null to use demo text.</summary>
    private PlayerSnapshot? LiveSnapshot() => _media?.Track is null ? null : Snapshot();

    public override void OnSettingsDraftChanged(string settingsJson)
    {
        _settings = WidgetSettings.FromJson(settingsJson);
        ApplyProviderSettings();
        _preview?.ApplySettings(_settings);
        Render();
        UpdateVisibility(force: true);
    }

    public void OnFlyoutShown()
    {
        _flyoutShown = true;
        _flyout?.OnShown();
        Render();
        UpdateTimer();
    }

    public void OnFlyoutHidden()
    {
        _flyoutShown = false;
        _flyout?.OnHidden();
        UpdateTimer();
    }

    // ---------------- lyrics ----------------

    private void OnTrackChanged()
    {
        var track = _media?.Track;
        var key = track is null ? null : $"{track.Title}\u001f{track.Artist}";
        if (key != _lyricsForKey)
        {
            _lyricsForKey = key;
            _ = LoadLyricsAsync(track, refetch: false);
        }
        Render();
        UpdateVisibility();
    }

    private void RefetchLyrics() => _ = LoadLyricsAsync(_media?.Track, refetch: true);

    private async Task LoadLyricsAsync(TrackInfo? track, bool refetch)
    {
        _lyricsCts?.Cancel();
        _lyrics = LyricsDocument.Empty;

        if (track is null || _lyricsService is null)
        {
            _status = LyricsStatus.Idle;
            Render();
            return;
        }

        var cts = _lyricsCts = new CancellationTokenSource();
        _status = LyricsStatus.Searching;
        Render();

        // Some players publish the title first and the duration a moment later;
        // a short wait gives us a better duration for matching.
        try { await Task.Delay(300, cts.Token); } catch (OperationCanceledException) { return; }
        var latest = _media?.Track ?? track;
        var query = new TrackQuery(latest.Title, latest.Artist, latest.Album, latest.Duration);

        try
        {
            if (refetch) _lyricsService.Invalidate(query);
            var doc = await Task.Run(() => _lyricsService.GetAsync(query, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _lyrics = doc;
            _status = doc.IsEmpty ? LyricsStatus.NotFound : LyricsStatus.Found;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Lyrics lookup failed: {ex.Message}");
            if (cts.IsCancellationRequested) return;
            _status = LyricsStatus.NotFound;
        }
        Render();
    }

    private void ApplyProviderSettings()
    {
        if (_lyricsService is null) return;
        _lyricsService.UseNetease = _settings.UseNetease;
        _lyricsService.UseLrclib = _settings.UseLrclib;
    }

    // ---------------- rendering ----------------

    private PlayerSnapshot Snapshot()
    {
        var media = _media;
        var track = media?.Track;
        var position = media?.GetPosition() ?? TimeSpan.Zero;
        var lyricPos = position + TimeSpan.FromMilliseconds(_settings.OffsetMs);
        var index = _lyrics.IndexAt(lyricPos);
        var progress = _lyrics.ProgressAt(index, lyricPos, track?.Duration ?? TimeSpan.Zero);
        return new PlayerSnapshot(track, media?.IsPlaying ?? false, media?.CanSeek ?? false,
            position, _lyrics, _status, index, progress);
    }

    private void Render()
    {
        if (_preview is null && _flyout is null) return;
        var snap = Snapshot();
        if (_preview is not null && _previewOnTop) _preview.Render(snap, _settings);
        if (_flyout is not null && _flyoutShown) _flyout.Render(snap, _settings);
    }

    /// <summary>Tick only while something on screen is moving.</summary>
    private void UpdateTimer()
    {
        if (_timer is null) return;
        var needed = (_media?.IsPlaying ?? false) && (_previewOnTop || _flyoutShown);
        if (needed && !_timer.IsEnabled) _timer.Start();
        else if (!needed && _timer.IsEnabled) _timer.Stop();
    }

    private void UpdateVisibility(bool force = false)
    {
        var visible = IsPreviewVisible;
        if (force || visible != _lastVisible)
        {
            _lastVisible = visible;
            Context?.RequestPreviewRefresh();
        }
    }

    private void OnPreviewVisibilityChanged(object? sender, bool isVisible)
    {
        _previewOnTop = isVisible;
        _preview?.SetActive(isVisible);
        if (isVisible) Render();
        UpdateTimer();
    }

    public override ValueTask DisposeAsync()
    {
        if (Context is not null) Context.PreviewVisibilityChanged -= OnPreviewVisibilityChanged;
        _lyricsCts?.Cancel();
        _timer?.Stop();
        _timer = null;
        _media?.Dispose();
        _media = null;
        _preview = null;
        _flyout = null;
        return ValueTask.CompletedTask;
    }
}

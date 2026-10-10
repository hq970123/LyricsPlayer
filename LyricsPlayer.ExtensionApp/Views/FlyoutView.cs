using LyricsPlayer.ExtensionApp.Lyrics;
using LyricsPlayer.ExtensionApp.Media;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LyricsPlayer.ExtensionApp.Views;

/// <summary>
/// Flyout: cover + title, a scrolling lyric list that follows playback (tap a line to
/// jump there), a seek bar and playback controls.
/// </summary>
public sealed class FlyoutView : Grid
{
    private readonly MediaSessionWatcher _media;
    private readonly Action _refetchLyrics;

    private readonly Image _artImage;
    private readonly FontIcon _artPlaceholder;
    private readonly MarqueeText _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _source;
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _lines;
    private readonly TextBlock _message;
    private readonly Slider _seek;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _total;
    private readonly Button _playPause;

    private LyricsDocument? _renderedDoc;
    private bool _renderedTranslation;
    private int _highlighted = -2;
    private string? _artKey;
    private bool _dragging;
    private DateTimeOffset _userScrolledAt = DateTimeOffset.MinValue;
    private Brush? _highlightBrush;
    private string? _highlightHex;

    public FlyoutView(MediaSessionWatcher media, Action refetchLyrics)
    {
        _media = media;
        _refetchLyrics = refetchLyrics;
        Padding = new Thickness(20, 20, 20, 16);
        RowSpacing = 12;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- Header
        _artImage = new Image { Stretch = Stretch.UniformToFill };
        _artPlaceholder = new FontIcon { Glyph = UiHelpers.GlyphMusic, FontSize = 24, Opacity = 0.5 };
        var artGrid = new Grid();
        artGrid.Children.Add(_artPlaceholder);
        artGrid.Children.Add(_artImage);
        var art = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(8), Child = artGrid };

        _title = new MarqueeText(16);
        _title.Inner.FontWeight = FontWeights.SemiBold;
        _artist = new TextBlock { FontSize = 13, Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        _source = new TextBlock { FontSize = 11, Opacity = 0.5, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        titles.Children.Add(_title);
        titles.Children.Add(_artist);
        titles.Children.Add(_source);

        var refresh = UiHelpers.IconButton(UiHelpers.GlyphRefresh, 32, 13, "重新获取歌词");
        refresh.VerticalAlignment = VerticalAlignment.Top;
        refresh.Click += (_, _) => _refetchLyrics();

        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(art);
        SetColumn(titles, 1); header.Children.Add(titles);
        SetColumn(refresh, 2); header.Children.Add(refresh);
        Children.Add(header);

        // ---- Lyrics
        _lines = new StackPanel { Spacing = 14, Padding = new Thickness(0, 120, 0, 160) };
        _scroller = new ScrollViewer
        {
            Content = _lines,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        // Pause auto-follow for a few seconds after the user scrolls by hand.
        _scroller.AddHandler(PointerWheelChangedEvent, new PointerEventHandler((_, _) => _userScrolledAt = DateTimeOffset.Now), true);
        _scroller.DirectManipulationStarted += (_, _) => _userScrolledAt = DateTimeOffset.Now;

        _message = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        var lyricArea = new Grid();
        lyricArea.Children.Add(_scroller);
        lyricArea.Children.Add(_message);
        SetRow(lyricArea, 1);
        Children.Add(lyricArea);

        // ---- Seek bar
        _seek = new Slider { Minimum = 0, Maximum = 1, StepFrequency = 1, IsThumbToolTipEnabled = false };
        _seek.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _dragging = true), true);
        _seek.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => CommitSeek()), true);
        _seek.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => CommitSeek()), true);
        _seek.ValueChanged += (_, e) => { if (_dragging) _elapsed.Text = UiHelpers.FormatTime(TimeSpan.FromSeconds(e.NewValue)); };
        _elapsed = new TextBlock { FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        _total = new TextBlock { FontSize = 11, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        var seekRow = new Grid { ColumnSpacing = 10 };
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.Children.Add(_elapsed);
        SetColumn(_seek, 1); seekRow.Children.Add(_seek);
        SetColumn(_total, 2); seekRow.Children.Add(_total);
        SetRow(seekRow, 2);
        Children.Add(seekRow);

        // ---- Controls
        var prev = UiHelpers.IconButton(UiHelpers.GlyphPrevious, 40, 16, "上一首");
        prev.Click += (_, _) => _media.Previous();
        _playPause = UiHelpers.IconButton(UiHelpers.GlyphPlay, 48, 20, "播放/暂停", filled: true);
        _playPause.Click += (_, _) => _media.TogglePlayPause();
        var next = UiHelpers.IconButton(UiHelpers.GlyphNext, 40, 16, "下一首");
        next.Click += (_, _) => _media.Next();
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, HorizontalAlignment = HorizontalAlignment.Center };
        controls.Children.Add(prev);
        controls.Children.Add(_playPause);
        controls.Children.Add(next);
        SetRow(controls, 3);
        Children.Add(controls);
    }

    private void CommitSeek()
    {
        if (!_dragging) return;
        _dragging = false;
        _media.Seek(TimeSpan.FromSeconds(_seek.Value));
    }

    public void Render(PlayerSnapshot s, WidgetSettings settings)
    {
        _title.ScrollEnabled = settings.ScrollLongTitle && _active;
        if (settings.HighlightColor != _highlightHex)
        {
            _highlightHex = settings.HighlightColor;
            _highlightBrush = settings.HighlightBrush;
            var current = _highlighted;
            _highlighted = -2;
            if (current >= 0) Highlight(current); // repaint the active line in the new color
        }

        UiHelpers.SetGlyph(_playPause, s.IsPlaying ? UiHelpers.GlyphPause : UiHelpers.GlyphPlay);

        if (s.Track is null)
        {
            _title.Text = "没有正在播放的音乐";
            _artist.Text = "在任意播放器里放首歌试试";
            _source.Text = "";
            ShowMessage("");
            BuildLines(LyricsDocument.Empty, false);
            _ = SetArtAsync(null);
            _seek.IsEnabled = false;
            _elapsed.Text = _total.Text = "0:00";
            return;
        }

        _title.Text = s.Track.Title;
        _artist.Text = string.Join(" · ", new[] { s.Track.Artist, s.Track.Album }.Where(x => x.Length > 0));
        var app = UiHelpers.FriendlyAppName(s.Track.SourceAppId);
        var lyricSource = s.Status == LyricsStatus.Found ? s.Lyrics.Source : "";
        _source.Text = string.Join("  ·  ", new[]
        {
            app.Length > 0 ? "来自 " + app : "",
            lyricSource.Length > 0 ? "歌词 " + lyricSource : "",
        }.Where(x => x.Length > 0));
        _ = SetArtAsync(s.Track);

        // Seek bar
        var duration = s.Track.Duration;
        _seek.IsEnabled = s.CanSeek && duration > TimeSpan.Zero;
        _seek.Maximum = Math.Max(1, duration.TotalSeconds);
        if (!_dragging)
        {
            _seek.Value = Math.Min(_seek.Maximum, s.Position.TotalSeconds);
            _elapsed.Text = UiHelpers.FormatTime(s.Position);
        }
        _total.Text = duration > TimeSpan.Zero ? UiHelpers.FormatTime(duration) : "--:--";

        // Lyrics
        switch (s.Status)
        {
            case LyricsStatus.Searching:
                ShowMessage("正在查找歌词…");
                BuildLines(LyricsDocument.Empty, false);
                return;
            case LyricsStatus.NotFound:
                ShowMessage("暂无歌词\n点右上角可重新获取");
                BuildLines(LyricsDocument.Empty, false);
                return;
        }

        ShowMessage("");
        BuildLines(s.Lyrics, settings.ShowTranslationInFlyout);
        Highlight(s.Lyrics.IsSynced ? s.LineIndex : -1);
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _message.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildLines(LyricsDocument doc, bool showTranslation)
    {
        if (ReferenceEquals(doc, _renderedDoc) && showTranslation == _renderedTranslation) return;
        _renderedDoc = doc;
        _renderedTranslation = showTranslation;
        _highlighted = -2;
        _lines.Children.Clear();
        _scroller.ChangeView(null, 0, null, true);

        for (int i = 0; i < doc.Lines.Count; i++)
        {
            var line = doc.Lines[i];
            var item = new StackPanel { Spacing = 2, Opacity = doc.IsSynced ? 0.45 : 0.85, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            item.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(line.Text) ? "♪" : line.Text,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.WrapWholeWords,
            });
            if (showTranslation && !string.IsNullOrEmpty(line.Translation))
            {
                item.Children.Add(new TextBlock { Text = line.Translation, FontSize = 12, TextWrapping = TextWrapping.WrapWholeWords });
            }

            if (doc.IsSynced)
            {
                var time = line.Time;
                item.Tapped += (_, _) => _media.Seek(time);
                ToolTipService.SetToolTip(item, UiHelpers.FormatTime(time));
            }
            _lines.Children.Add(item);
        }
    }

    private void Highlight(int index)
    {
        if (index == _highlighted) return;

        if (_highlighted >= 0 && _highlighted < _lines.Children.Count && _lines.Children[_highlighted] is StackPanel old)
        {
            old.Opacity = 0.45;
            if (old.Children[0] is TextBlock oldText) oldText.ClearValue(TextBlock.ForegroundProperty);
            old.Scale = new System.Numerics.Vector3(1, 1, 1);
        }
        _highlighted = index;
        if (index < 0 || index >= _lines.Children.Count || _lines.Children[index] is not StackPanel item) return;

        item.Opacity = 1;
        if (item.Children[0] is TextBlock activeText)
        {
            if (_highlightBrush is null) activeText.ClearValue(TextBlock.ForegroundProperty);
            else activeText.Foreground = _highlightBrush;
        }
        // Gentle grow on the active line; ScaleTransition animates the change.
        item.ScaleTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(200) };
        item.CenterPoint = new System.Numerics.Vector3(0, (float)item.ActualHeight / 2, 0);
        item.Scale = new System.Numerics.Vector3(1.04f, 1.04f, 1);

        if (DateTimeOffset.Now - _userScrolledAt < TimeSpan.FromSeconds(4)) return;
        var y = item.TransformToVisual(_lines).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        var target = y + item.ActualHeight / 2 - _scroller.ViewportHeight / 2;
        _scroller.ChangeView(null, Math.Max(0, target), null, false);
    }

    private bool _active;

    /// <summary>Pauses the title marquee while the flyout is hidden.</summary>
    public void OnHidden()
    {
        _active = false;
        _title.ScrollEnabled = false;
    }

    /// <summary>Re-center on the current line when the flyout reopens.</summary>
    public void OnShown()
    {
        _active = true;
        _userScrolledAt = DateTimeOffset.MinValue;
        var current = _highlighted;
        _highlighted = -2;
        if (current >= 0) Highlight(current);
    }

    private async Task SetArtAsync(TrackInfo? track)
    {
        var key = track is null ? null : track.Key + (track.Thumbnail?.Length ?? 0);
        if (key == _artKey) return;
        _artKey = key;
        var img = await UiHelpers.ToImageAsync(track?.Thumbnail, 128);
        if (_artKey != key) return;
        _artImage.Source = img;
        _artPlaceholder.Visibility = img is null ? Visibility.Visible : Visibility.Collapsed;
    }
}

using LyricsPlayer.ExtensionApp.Lyrics;
using LyricsPlayer.ExtensionApp.Media;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace LyricsPlayer.ExtensionApp.Views;

/// <summary>
/// Flyout: cover + title with a "歌词" pill (re-fetch), a lyric card that follows playback
/// (tap a line to jump there), a per-song offset nudger, the seek bar, and
/// shuffle / previous / play-pause / next / repeat.
/// </summary>
public sealed class FlyoutView : Grid
{
    private readonly MediaSessionWatcher _media;
    private readonly Action _refetchLyrics;
    private readonly Action<int> _nudgeOffset;

    private readonly Image _artImage;
    private readonly FontIcon _artPlaceholder;
    private readonly MarqueeText _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _source;
    private readonly TextBlock _offsetText;
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _lines;
    private readonly TextBlock _message;
    private readonly Slider _seek;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _total;
    private readonly Button _playPause;
    private readonly Button _shuffle;
    private readonly Button _repeat;
    private Brush? _accentText;

    private LyricsDocument? _renderedDoc;
    private bool _renderedTranslation;
    private int _highlighted = -2;
    private string? _artKey;
    private bool _dragging;
    private DateTimeOffset _userScrolledAt = DateTimeOffset.MinValue;
    private Brush? _highlightBrush;
    private string? _highlightHex;

    /// <param name="nudgeOffset">Shift this song's lyric timing by the given milliseconds (0 = reset).</param>
    public FlyoutView(MediaSessionWatcher media, Action refetchLyrics, Action<int> nudgeOffset)
    {
        _media = media;
        _refetchLyrics = refetchLyrics;
        _nudgeOffset = nudgeOffset;
        Padding = new Thickness(16, 14, 16, 12);
        RowSpacing = 10;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // header
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // lyrics card
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // offset + source
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // seek
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // controls

        // ---- Header: cover, title / artist, "歌词" pill. WidBar puts its gear in the top-right corner.
        _artImage = new Image { Stretch = Stretch.UniformToFill };
        _artPlaceholder = new FontIcon { Glyph = UiHelpers.GlyphMusic, FontSize = 20, Opacity = 0.5 };
        var artGrid = new Grid();
        artGrid.Children.Add(_artPlaceholder);
        artGrid.Children.Add(_artImage);
        var art = new Border { Width = 48, Height = 48, CornerRadius = new CornerRadius(6), Child = artGrid };

        _title = new MarqueeText(16);
        _title.Inner.FontWeight = FontWeights.SemiBold;
        _artist = new TextBlock { FontSize = 13, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        titles.Children.Add(_title);
        titles.Children.Add(_artist);

        var pillContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        pillContent.Children.Add(new FontIcon { Glyph = UiHelpers.GlyphRefresh, FontSize = 13 });
        pillContent.Children.Add(new TextBlock { Text = "歌词", FontWeight = FontWeights.SemiBold });
        var pill = new Button
        {
            Content = pillContent,
            Height = 34,
            Padding = new Thickness(14, 0, 16, 0),
            CornerRadius = new CornerRadius(17),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 32, 0), // clear of WidBar's settings gear
        };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accentStyle) && accentStyle is Style st) pill.Style = st;
        ToolTipService.SetToolTip(pill, "重新获取歌词");
        pill.Click += (_, _) => _refetchLyrics();

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(art);
        SetColumn(titles, 1); header.Children.Add(titles);
        SetColumn(pill, 2); header.Children.Add(pill);
        Children.Add(header);

        // ---- Lyrics card
        _lines = new StackPanel { Spacing = 10 };
        _scroller = new ScrollViewer
        {
            Content = _lines,
            Padding = new Thickness(14, 0, 14, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        };
        // Room above the first and below the last line so any line can sit in the middle.
        _scroller.SizeChanged += (_, e) =>
        {
            var half = Math.Max(0, e.NewSize.Height / 2 - 12);
            _lines.Padding = new Thickness(0, half, 0, half);
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
        var lyricGrid = new Grid();
        lyricGrid.Children.Add(_scroller);
        lyricGrid.Children.Add(_message);
        var lyricCard = new Border
        {
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = lyricGrid,
        };
        SetRow(lyricCard, 1);
        Children.Add(lyricCard);

        // ---- Offset nudger + lyric source
        var minus = SmallBox(UiHelpers.GlyphMinus, "歌词延后 0.5 秒（只对这首歌）");
        minus.Click += (_, _) => _nudgeOffset(-500);
        var plus = SmallBox(UiHelpers.GlyphAdd, "歌词提前 0.5 秒（只对这首歌）");
        plus.Click += (_, _) => _nudgeOffset(500);
        _offsetText = new TextBlock { Text = "0.0 s", FontSize = 13 };
        var offsetValue = new Button
        {
            Content = _offsetText, Height = 30, MinWidth = 72, Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(4),
        };
        ToolTipService.SetToolTip(offsetValue, "这首歌的歌词偏移，点击归零");
        offsetValue.Click += (_, _) => _nudgeOffset(0);
        var offsetPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        offsetPanel.Children.Add(minus);
        offsetPanel.Children.Add(offsetValue);
        offsetPanel.Children.Add(plus);

        _source = new TextBlock
        {
            FontSize = 12, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
        };
        var offsetRow = new Grid { ColumnSpacing = 10 };
        offsetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        offsetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        offsetRow.Children.Add(offsetPanel);
        SetColumn(_source, 1); offsetRow.Children.Add(_source);
        SetRow(offsetRow, 2);
        Children.Add(offsetRow);

        // ---- Seek bar
        _seek = new Slider { Minimum = 0, Maximum = 1, StepFrequency = 1, IsThumbToolTipEnabled = false, VerticalAlignment = VerticalAlignment.Center };
        _seek.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => _dragging = true), true);
        _seek.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => CommitSeek()), true);
        _seek.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => CommitSeek()), true);
        _seek.ValueChanged += (_, e) => { if (_dragging) _elapsed.Text = UiHelpers.FormatTime(TimeSpan.FromSeconds(e.NewValue)); };
        _elapsed = new TextBlock { FontSize = 12, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, MinWidth = 32 };
        _total = new TextBlock { FontSize = 12, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, MinWidth = 32, TextAlignment = TextAlignment.Right };
        var seekRow = new Grid { ColumnSpacing = 12 };
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        seekRow.Children.Add(_elapsed);
        SetColumn(_seek, 1); seekRow.Children.Add(_seek);
        SetColumn(_total, 2); seekRow.Children.Add(_total);
        SetRow(seekRow, 3);
        Children.Add(seekRow);

        // ---- Controls
        _shuffle = UiHelpers.IconButton(UiHelpers.GlyphShuffle, 36, 15, "随机播放");
        _shuffle.Click += (_, _) => _media.ToggleShuffle();
        var prev = UiHelpers.IconButton(UiHelpers.GlyphPrevious, 40, 18, "上一首");
        prev.Click += (_, _) => _media.Previous();
        _playPause = UiHelpers.IconButton(UiHelpers.GlyphPlay, 52, 22, "播放/暂停", accent: true);
        _playPause.Click += (_, _) => _media.TogglePlayPause();
        var next = UiHelpers.IconButton(UiHelpers.GlyphNext, 40, 18, "下一首");
        next.Click += (_, _) => _media.Next();
        _repeat = UiHelpers.IconButton(UiHelpers.GlyphRepeatAll, 36, 15, "循环播放");
        _repeat.Click += (_, _) => _media.CycleRepeat();
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
        };
        controls.Children.Add(_shuffle);
        controls.Children.Add(prev);
        controls.Children.Add(_playPause);
        controls.Children.Add(next);
        controls.Children.Add(_repeat);
        SetRow(controls, 4);
        Children.Add(controls);
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    /// <summary>A small square bordered button like the -/+ around the offset value.</summary>
    private static Button SmallBox(string glyph, string tooltip)
    {
        var b = new Button
        {
            Width = 34, Height = 30, Padding = new Thickness(0), CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
        };
        ToolTipService.SetToolTip(b, tooltip);
        return b;
    }

    private void UpdateModeButtons()
    {
        _accentText ??= ThemeBrush("AccentTextFillColorPrimaryBrush");

        _shuffle.IsEnabled = _media.CanShuffle;
        SetIconColor(_shuffle, _media.CanShuffle && _media.IsShuffleOn);
        ToolTipService.SetToolTip(_shuffle, _media.CanShuffle ? (_media.IsShuffleOn ? "随机播放：开" : "随机播放：关") : "这个播放器不支持切换随机播放");

        _repeat.IsEnabled = _media.CanRepeat;
        var mode = _media.RepeatMode;
        UiHelpers.SetGlyph(_repeat, mode == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? UiHelpers.GlyphRepeatOne : UiHelpers.GlyphRepeatAll);
        SetIconColor(_repeat, _media.CanRepeat && mode != Windows.Media.MediaPlaybackAutoRepeatMode.None);
        ToolTipService.SetToolTip(_repeat, !_media.CanRepeat ? "这个播放器不支持切换循环" : mode switch
        {
            Windows.Media.MediaPlaybackAutoRepeatMode.List => "列表循环",
            Windows.Media.MediaPlaybackAutoRepeatMode.Track => "单曲循环",
            _ => "循环：关",
        });
    }

    private void SetIconColor(Button b, bool active)
    {
        if (b.Content is not FontIcon icon) return;
        if (active && _accentText is not null) icon.Foreground = _accentText;
        else icon.ClearValue(IconElement.ForegroundProperty);
    }

    private void CommitSeek()
    {
        if (!_dragging) return;
        _dragging = false;
        _media.Seek(TimeSpan.FromSeconds(_seek.Value));
    }

    public void Render(PlayerSnapshot s, WidgetSettings settings, int songOffsetMs = 0)
    {
        _offsetText.Text = songOffsetMs == 0 ? "0.0 s" : $"{songOffsetMs / 1000.0:+0.0;-0.0} s";
        UpdateModeButtons();
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
        _artist.Text = s.Track.Artist;
        var lyricSource = s.Status == LyricsStatus.Found ? s.Lyrics.Source : "";
        _source.Text = lyricSource.Length > 0 ? "歌词来自 " + lyricSource : "";
        var app = UiHelpers.FriendlyAppName(s.Track.SourceAppId);
        ToolTipService.SetToolTip(_source, app.Length > 0 ? "正在 " + app + " 播放" : null);
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
                ShowMessage("暂无歌词\n点右上角“歌词”可重新获取");
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
            if (old.Children[0] is TextBlock oldText)
            {
                oldText.ClearValue(TextBlock.ForegroundProperty);
                oldText.FontWeight = FontWeights.Normal;
            }
            old.Scale = new System.Numerics.Vector3(1, 1, 1);
        }
        _highlighted = index;
        if (index < 0 || index >= _lines.Children.Count || _lines.Children[index] is not StackPanel item) return;

        item.Opacity = 1;
        if (item.Children[0] is TextBlock activeText)
        {
            activeText.FontWeight = FontWeights.SemiBold;
            if (_highlightBrush is null) activeText.ClearValue(TextBlock.ForegroundProperty);
            else activeText.Foreground = _highlightBrush;
        }
        // Gentle grow on the active line; ScaleTransition animates the change.
        item.ScaleTransition ??= new Vector3Transition { Duration = TimeSpan.FromMilliseconds(200) };
        item.CenterPoint = new System.Numerics.Vector3(0, (float)item.ActualHeight / 2, 0);
        item.Scale = new System.Numerics.Vector3(1.03f, 1.03f, 1);

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

using LyricsPlayer.ExtensionApp.Media;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace LyricsPlayer.ExtensionApp.Views;

/// <summary>
/// Taskbar preview: [cover] [title - artist / current lyric with karaoke wipe] [prev] [play/pause] [next]
/// Long song names scroll (marquee); long lyric lines slide along with the wipe so the
/// part being sung stays in view.
/// </summary>
public sealed class PreviewView : Grid
{
    private readonly Border _art;
    private readonly Image _artImage;
    private readonly FontIcon _artPlaceholder;
    private readonly MarqueeText _topLine;
    private readonly MarqueeText _bigTitle;

    private readonly Grid _lyricHost;
    private readonly RectangleGeometry _lyricHostClip = new();
    private readonly TranslateTransform _lyricShift = new();
    private readonly TextBlock _lyricBase;
    private readonly TextBlock _lyricFill;
    private readonly RectangleGeometry _fillClip = new();
    private double _lastProgress;

    private readonly StackPanel _controls;
    private readonly Button _playPause;
    private string? _artKey;
    private bool _active = true;
    private WidgetSettings _settings = new();

    public PreviewView(MediaSessionWatcher media)
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Padding = new Thickness(4, 0, 4, 0);
        ColumnSpacing = 10;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // ---- Cover
        _artImage = new Image { Stretch = Stretch.UniformToFill };
        _artPlaceholder = new FontIcon { Glyph = UiHelpers.GlyphMusic, FontSize = 16, Opacity = 0.6 };
        var artGrid = new Grid();
        artGrid.Children.Add(_artPlaceholder);
        artGrid.Children.Add(_artImage);
        _art = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(6),
            VerticalAlignment = VerticalAlignment.Center,
            Child = artGrid,
        };
        SetColumn(_art, 0);
        Children.Add(_art);

        // ---- Text column
        _topLine = new MarqueeText(12);

        // Lyric line: two identical TextBlocks, the top one clipped to the sung width.
        _lyricBase = LyricBlock();
        _lyricFill = LyricBlock();
        _lyricFill.Clip = _fillClip;
        var lyricGroup = new Grid { RenderTransform = _lyricShift };
        lyricGroup.Children.Add(_lyricBase);
        lyricGroup.Children.Add(_lyricFill);
        var lyricCanvas = new Canvas { Height = 20 }; // Canvas lets the text be wider than the slot
        lyricCanvas.Children.Add(lyricGroup);
        _lyricHost = new Grid { Clip = _lyricHostClip };
        _lyricHost.Children.Add(lyricCanvas);
        _lyricHost.SizeChanged += (_, _) => UpdateLyricLayout(_lastProgress);
        _lyricFill.SizeChanged += (_, _) =>
        {
            lyricCanvas.Height = _lyricFill.ActualHeight;
            UpdateLyricLayout(_lastProgress);
        };

        // Shown instead of the lyric line when there are no synced lyrics.
        _bigTitle = new MarqueeText(14) { Visibility = Visibility.Collapsed };
        _bigTitle.Inner.FontWeight = FontWeights.SemiBold;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(_topLine);
        text.Children.Add(_lyricHost);
        text.Children.Add(_bigTitle);
        SetColumn(text, 1);
        Children.Add(text);

        // ---- Controls (clicking them does not open the flyout)
        var prev = UiHelpers.IconButton(UiHelpers.GlyphPrevious, 32, 14, "上一首");
        prev.Click += (_, _) => media.Previous();
        _playPause = UiHelpers.IconButton(UiHelpers.GlyphPlay, 36, 16, "播放/暂停", filled: true);
        _playPause.Click += (_, _) => media.TogglePlayPause();
        var next = UiHelpers.IconButton(UiHelpers.GlyphNext, 32, 14, "下一首");
        next.Click += (_, _) => media.Next();

        _controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _controls.Children.Add(prev);
        _controls.Children.Add(_playPause);
        _controls.Children.Add(next);
        SetColumn(_controls, 2);
        Children.Add(_controls);
    }

    private static TextBlock LyricBlock() => new()
    {
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    public void ApplySettings(WidgetSettings settings)
    {
        _settings = settings;
        _art.Visibility = settings.ShowAlbumArt ? Visibility.Visible : Visibility.Collapsed;
        _controls.Visibility = settings.ShowControls ? Visibility.Visible : Visibility.Collapsed;

        var text = settings.TextBrush;
        var highlight = settings.HighlightBrush;

        _topLine.Foreground = text;
        _topLine.Opacity = text is null ? 0.6 : 0.8;
        _bigTitle.Foreground = text;
        SetForeground(_lyricBase, text);
        // Two custom colors = a real karaoke pair, show both at full strength.
        _lyricBase.Opacity = text is not null && highlight is not null ? 1.0 : 0.5;
        SetForeground(_lyricFill, highlight ?? text);
        if (text is null) _artPlaceholder.ClearValue(IconElement.ForegroundProperty);
        else _artPlaceholder.Foreground = text;
        foreach (var child in _controls.Children)
        {
            if (child is Button { Content: FontIcon icon })
            {
                if (text is null) icon.ClearValue(IconElement.ForegroundProperty);
                else icon.Foreground = text;
            }
        }

        UpdateScrolling();
    }

    /// <summary>Pause marquee animations while another smart-stack member is shown.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        UpdateScrolling();
    }

    private void UpdateScrolling()
    {
        var on = _active && _settings.ScrollLongTitle;
        _topLine.ScrollEnabled = on;
        _bigTitle.ScrollEnabled = on;
    }

    public void Render(PlayerSnapshot s, WidgetSettings settings)
    {
        UiHelpers.SetGlyph(_playPause, s.IsPlaying ? UiHelpers.GlyphPause : UiHelpers.GlyphPlay);

        if (s.Track is null)
        {
            _topLine.Text = "Lyrics Player";
            ShowBigTitle("没有正在播放的音乐");
            _ = SetArtAsync(null);
            return;
        }

        var titleArtist = s.Track.Artist.Length > 0 ? $"{s.Track.Title} - {s.Track.Artist}" : s.Track.Title;
        _ = SetArtAsync(s.Track);

        if (s.Status == LyricsStatus.Found && s.Lyrics.IsSynced)
        {
            var line = s.LineIndex >= 0 ? s.Lyrics.Lines[s.LineIndex] : null;
            var lyric = line is null || string.IsNullOrWhiteSpace(line.Text) ? "♪ ♪ ♪" : line.Text;
            _topLine.Text = settings.TranslationOnTopLine && line?.Translation is { Length: > 0 } tr ? tr : titleArtist;
            ShowLyric(lyric, line is null ? 0 : s.LineProgress);
        }
        else
        {
            // No synced lyrics: the song name takes the big line (and scrolls if long).
            var status = s.Status switch
            {
                LyricsStatus.Searching => "正在查找歌词…",
                LyricsStatus.NotFound => "暂无歌词",
                _ => s.Lyrics.IsEmpty ? "" : "歌词未同步",
            };
            _topLine.Text = string.Join(" · ", new[] { s.Track.Artist, status }.Where(x => x.Length > 0));
            ShowBigTitle(s.Track.Title);
        }
    }

    private void ShowBigTitle(string title)
    {
        _bigTitle.Text = title;
        _bigTitle.Visibility = Visibility.Visible;
        _lyricHost.Visibility = Visibility.Collapsed;
    }

    private void ShowLyric(string text, double progress)
    {
        _bigTitle.Visibility = Visibility.Collapsed;
        _lyricHost.Visibility = Visibility.Visible;
        if (_lyricBase.Text != text)
        {
            _lyricBase.Text = text;
            _lyricFill.Text = text;
        }
        UpdateLyricLayout(progress);
    }

    private void UpdateLyricLayout(double progress)
    {
        _lastProgress = progress;
        var textWidth = _lyricFill.ActualWidth;
        var height = Math.Max(_lyricFill.ActualHeight, 20);
        var sung = textWidth * progress;
        _fillClip.Rect = new Rect(0, 0, Math.Max(0, sung), height);

        var available = _lyricHost.ActualWidth;
        _lyricHostClip.Rect = new Rect(0, 0, Math.Max(0, available), height);

        // Line wider than the slot: slide it so the sung edge sits ~65% across.
        double shift = 0;
        if (available > 0 && textWidth > available)
        {
            shift = Math.Clamp(sung - available * 0.65, 0, textWidth - available);
        }
        _lyricShift.X = -shift;
    }

    private static void SetForeground(TextBlock block, Brush? brush)
    {
        if (brush is null) block.ClearValue(TextBlock.ForegroundProperty);
        else block.Foreground = brush;
    }

    private async Task SetArtAsync(TrackInfo? track)
    {
        var key = track is null ? null : track.Key + (track.Thumbnail?.Length ?? 0);
        if (key == _artKey) return;
        _artKey = key;
        var img = await UiHelpers.ToImageAsync(track?.Thumbnail, 72);
        if (_artKey != key) return; // a newer song arrived while decoding
        _artImage.Source = img;
        _artPlaceholder.Visibility = img is null ? Visibility.Visible : Visibility.Collapsed;
    }
}

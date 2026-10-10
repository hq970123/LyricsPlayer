using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using LyricsPlayer.ExtensionApp.Lyrics;
using LyricsPlayer.ExtensionApp.Media;
using WidBar.SDK;

namespace LyricsPlayer.ExtensionApp.Views;

/// <summary>Settings page hosted by WidBar (it adds Save / Cancel).</summary>
public sealed class SettingsView : StackPanel
{
    private readonly WidgetSettings _draft;
    private readonly Border _sampleSlot;
    private readonly PreviewView _sample;
    private readonly TextBlock _widthNote;
    private readonly Func<PlayerSnapshot?> _live;
    private readonly Func<double> _actualWidth;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Stopwatch _demoClock = Stopwatch.StartNew();

    public SettingsView(IWidgetSettingsContext context, MediaSessionWatcher? media,
        Func<PlayerSnapshot?> live, Func<double> actualWidth)
    {
        var draft = _draft = WidgetSettings.FromJson(context.SettingsJson);
        _live = live;
        _actualWidth = actualWidth;
        Spacing = 16;

        // ---- Live sample of the taskbar slot, redrawn on every change below.
        _sample = new PreviewView(media);
        _sample.ApplySettings(draft);
        _sampleSlot = new Border
        {
            Width = draft.PreviewWidth,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(6),
            Background = ThemeBrush("SolidBackgroundFillColorBaseBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = _sample,
        };
        var sampleScroll = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _sampleSlot,
        };
        _widthNote = new TextBlock { FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap };

        var samplePanel = new StackPanel { Spacing = 6 };
        samplePanel.Children.Add(new TextBlock
        {
            Text = "效果预览",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        samplePanel.Children.Add(sampleScroll);
        samplePanel.Children.Add(_widthNote);
        Children.Add(samplePanel);

        Loaded += (_, _) => { _sample.SetActive(true); _timer.Start(); Tick(); };
        Unloaded += (_, _) => { _timer.Stop(); _sample.SetActive(false); };
        _timer.Tick += (_, _) => Tick();

        void Save()
        {
            context.SaveSettings(draft.ToJson());
            context.RequestPreviewRefresh();
            _sampleSlot.Width = draft.PreviewWidth;
            _sample.ApplySettings(draft);
            Tick();
        }

        Children.Add(Header("布局"));
        Children.Add(IntSlider("任务栏宽度", "px", WidgetSettings.MinWidth, WidgetSettings.MaxWidth, 10,
            draft.PreviewWidth, v => { draft.PreviewWidth = v; Save(); }));
        Children.Add(IntSlider("歌词字号", "", 10, 26, 1, draft.LyricFontSize, v => { draft.LyricFontSize = v; Save(); }));
        Children.Add(Toggle("歌词上方显示歌名行（关闭后只显示一行歌词，字可以更大）", draft.ShowTopLine,
            v => { draft.ShowTopLine = v; Save(); }));
        Children.Add(IntSlider("歌名字号", "", 9, 20, 1, draft.TitleFontSize, v => { draft.TitleFontSize = v; Save(); }));
        Children.Add(IntSlider("元素间距", "px", 0, 20, 1, draft.Spacing, v => { draft.Spacing = v; Save(); }));

        Children.Add(Toggle("显示专辑封面", draft.ShowAlbumArt, v => { draft.ShowAlbumArt = v; Save(); }));
        Children.Add(IntSlider("封面大小", "px", 20, 48, 2, draft.CoverSize, v => { draft.CoverSize = v; Save(); }));

        var controls = new ComboBox { Header = "播放按钮", MinWidth = 220 };
        controls.Items.Add("上一首 / 播放暂停 / 下一首");
        controls.Items.Add("只显示播放暂停");
        controls.Items.Add("不显示");
        controls.SelectedIndex = (int)draft.Controls;
        controls.SelectionChanged += (_, _) =>
        {
            if (controls.SelectedIndex < 0) return;
            draft.Controls = (ControlsLayout)controls.SelectedIndex;
            Save();
        };
        Children.Add(controls);
        Children.Add(IntSlider("按钮大小", "px", 24, 40, 2, draft.ButtonSize, v => { draft.ButtonSize = v; Save(); }));

        Children.Add(Header("显示"));
        Children.Add(Toggle("有翻译时，第一行显示翻译（代替歌名）", draft.TranslationOnTopLine, v => { draft.TranslationOnTopLine = v; Save(); }));
        Children.Add(Toggle("没在播放时从任务栏隐藏", draft.HideWhenIdle, v => { draft.HideWhenIdle = v; Save(); }));
        Children.Add(Toggle("歌名过长时滚动显示", draft.ScrollLongTitle, v => { draft.ScrollLongTitle = v; Save(); }));

        Children.Add(Header("字体颜色"));
        Children.Add(ColorRow("文字颜色", "歌名、歌手和还没唱到的歌词", draft.TextColor, v => { draft.TextColor = v; Save(); }));
        Children.Add(ColorRow("歌词高亮色", "正在唱的部分，弹出面板里的当前句也用这个颜色", draft.HighlightColor, v => { draft.HighlightColor = v; Save(); }));

        Children.Add(Header("歌词"));
        Children.Add(Toggle("弹出面板显示翻译", draft.ShowTranslationInFlyout, v => { draft.ShowTranslationInFlyout = v; Save(); }));

        var offset = new Slider
        {
            Header = OffsetLabel(draft.OffsetMs),
            Minimum = -3000, Maximum = 3000, StepFrequency = 100, Value = draft.OffsetMs,
        };
        offset.ValueChanged += (_, e) =>
        {
            draft.OffsetMs = (int)e.NewValue;
            offset.Header = OffsetLabel(draft.OffsetMs);
            Save();
        };
        Children.Add(offset);

        Children.Add(Header("歌词来源"));
        Children.Add(Toggle("网易云音乐（中文歌优先）", draft.UseNetease, v => { draft.UseNetease = v; Save(); }));
        Children.Add(Toggle("LRCLIB（开源歌词库，英文歌优先）", draft.UseLrclib, v => { draft.UseLrclib = v; Save(); }));
    }

    /// <summary>One frame of the sample: the real song when something is playing, otherwise demo lyrics.</summary>
    private void Tick()
    {
        var snap = _live() ?? DemoSnapshot();
        _sample.Render(snap, _draft);

        var actual = _actualWidth();
        _widthNote.Text = actual > 0 && actual < _draft.PreviewWidth - 2
            ? $"任务栏上实际只有 {actual:0}px（任务栏空间不够时 WidBar 会自动缩小）。可以调小字号、按钮和封面，或者在任务栏上腾出位置。"
            : "调整下面的选项，这里和任务栏会立即变化；点“保存”后生效，点“取消”会恢复原样。";
    }

    private static readonly TrackInfo DemoTrack =
        new("晴天", "周杰伦", "叶惠美", TimeSpan.FromSeconds(269), "", null);

    private static readonly LyricsDocument DemoLyrics = new(new[]
    {
        new LyricLine(TimeSpan.Zero, "故事的小黄花 从出生那年就飘着", "The little yellow flower of the story"),
    }, true, "demo");

    private PlayerSnapshot DemoSnapshot()
    {
        // Sing the line over 4 s, hold 1 s, repeat.
        var t = _demoClock.Elapsed.TotalSeconds % 5.0;
        var progress = Math.Min(1.0, t / 4.0);
        return new PlayerSnapshot(DemoTrack, true, false, TimeSpan.FromSeconds(t), DemoLyrics,
            LyricsStatus.Found, 0, progress);
    }

    private static UIElement IntSlider(string label, string unit, int min, int max, int step, int value, Action<int> changed)
    {
        string Caption(int v) => $"{label}：{v}{unit}";
        var slider = new Slider
        {
            Header = Caption(value),
            Minimum = min, Maximum = max, StepFrequency = step, SmallChange = step, Value = value,
        };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            slider.Header = Caption(v);
            changed(v);
        };
        return slider;
    }

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    private static string OffsetLabel(int ms) => ms switch
    {
        0 => "歌词偏移：0（不调整）",
        > 0 => $"歌词偏移：提前 {ms / 1000.0:0.0} 秒",
        _ => $"歌词偏移：延后 {-ms / 1000.0:0.0} 秒",
    };

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        Margin = new Thickness(0, 8, 0, -8),
    };

    private static readonly string[] Presets =
    {
        "#FFFFFF", "#000000", "#31C27C", "#E60026", "#FF6B81", "#FFB300", "#4FC3F7", "#7C4DFF",
    };

    /// <summary>
    /// A color setting: label, a swatch button that opens a full color picker with presets,
    /// and a "跟随系统" button that clears the custom color (back to the Windows theme).
    /// </summary>
    private static UIElement ColorRow(string label, string description, string? current, Action<string?> changed)
    {
        var swatch = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x40, 0x80, 0x80, 0x80)),
        };
        var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        var buttonContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonContent.Children.Add(swatch);
        buttonContent.Children.Add(hexText);

        void Show(string? hex)
        {
            var c = WidgetSettings.ParseColor(hex);
            swatch.Background = c is { } color ? new SolidColorBrush(color) : null;
            hexText.Text = c is null ? "跟随系统" : hex!.ToUpperInvariant();
        }
        Show(current);

        var picker = new ColorPicker
        {
            IsAlphaEnabled = false,
            IsMoreButtonVisible = false,
            IsColorSliderVisible = true,
            IsHexInputVisible = true,
            Color = WidgetSettings.ParseColor(current) ?? Microsoft.UI.Colors.White,
        };
        bool suppress = false;
        picker.ColorChanged += (_, e) =>
        {
            if (suppress) return;
            var hex = WidgetSettings.ToHex(e.NewColor);
            Show(hex);
            changed(hex);
        };

        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var p in Presets)
        {
            var color = WidgetSettings.ParseColor(p)!.Value;
            var chip = new Button
            {
                Width = 24, Height = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(color),
            };
            chip.Click += (_, _) => picker.Color = color; // ColorChanged saves it
            presets.Children.Add(chip);
        }

        var pickerPanel = new StackPanel { Spacing = 12, Padding = new Thickness(4) };
        pickerPanel.Children.Add(presets);
        pickerPanel.Children.Add(picker);

        var button = new DropDownButton { Content = buttonContent, Flyout = new Flyout { Content = pickerPanel } };

        var reset = new HyperlinkButton { Content = "跟随系统", VerticalAlignment = VerticalAlignment.Center };
        reset.Click += (_, _) =>
        {
            suppress = true;
            picker.Color = Microsoft.UI.Colors.White;
            suppress = false;
            Show(null);
            changed(null);
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(button);
        controls.Children.Add(reset);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = label });
        panel.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(controls);
        return panel;
    }

    private static ToggleSwitch Toggle(string header, bool value, Action<bool> changed)
    {
        var t = new ToggleSwitch { Header = header, IsOn = value };
        t.Toggled += (_, _) => changed(t.IsOn);
        return t;
    }
}

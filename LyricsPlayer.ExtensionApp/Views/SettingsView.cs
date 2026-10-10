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
        Spacing = 12;

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

        var samplePanel = new StackPanel { Spacing = 4 };
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

        // ---- Compact sections: one row per setting (label left, control right).
                UIElement coverSizeRow = null!, titleSizeRow = null!, buttonSizeRow = null!;

        var (layout, layoutCard) = Card("布局");
        layout.Children.Add(SliderRow("任务栏宽度", "px", WidgetSettings.MinWidth, WidgetSettings.MaxWidth, 10,
            draft.PreviewWidth, v => { draft.PreviewWidth = v; Save(); }));
        layout.Children.Add(SliderRow("元素间距", "px", 0, 20, 1, draft.Spacing, v => { draft.Spacing = v; Save(); }));
        layout.Children.Add(ToggleRow("显示专辑封面", draft.ShowAlbumArt, v =>
        {
            draft.ShowAlbumArt = v; Enable(coverSizeRow, v); Save();
        }, out _));
        layout.Children.Add(coverSizeRow = SliderRow("封面大小", "px", 20, 48, 2, draft.CoverSize, v => { draft.CoverSize = v; Save(); }));

        var controls = new ComboBox { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Items.Add("全部按钮");
        controls.Items.Add("只有播放暂停");
        controls.Items.Add("不显示");
        controls.SelectedIndex = (int)draft.Controls;
        controls.SelectionChanged += (_, _) =>
        {
            if (controls.SelectedIndex < 0) return;
            draft.Controls = (ControlsLayout)controls.SelectedIndex;
            Enable(buttonSizeRow, draft.Controls != ControlsLayout.None);
            Save();
        };
        layout.Children.Add(Row("播放按钮", controls));
        layout.Children.Add(buttonSizeRow = SliderRow("按钮大小", "px", 24, 40, 2, draft.ButtonSize, v => { draft.ButtonSize = v; Save(); }));
        Children.Add(layoutCard);

        var (text, textCard) = Card("文字");
        text.Children.Add(SliderRow("歌词字号", "", 10, 26, 1, draft.LyricFontSize, v => { draft.LyricFontSize = v; Save(); }));
        text.Children.Add(ToggleRow("显示歌名行", draft.ShowTopLine, v =>
        {
            draft.ShowTopLine = v; Enable(titleSizeRow, v); Save();
        }, out _, "关闭后只显示一行歌词，字可以更大"));
        text.Children.Add(titleSizeRow = SliderRow("歌名字号", "", 9, 20, 1, draft.TitleFontSize, v => { draft.TitleFontSize = v; Save(); }));
        text.Children.Add(ToggleRow("歌名过长时滚动", draft.ScrollLongTitle, v => { draft.ScrollLongTitle = v; Save(); }, out _));
        text.Children.Add(ToggleRow("有翻译时歌名行显示翻译", draft.TranslationOnTopLine, v => { draft.TranslationOnTopLine = v; Save(); }, out _));
        text.Children.Add(ColorRow("文字颜色", "歌名、歌手和还没唱到的歌词", draft.TextColor, v => { draft.TextColor = v; Save(); }));
        text.Children.Add(ColorRow("高亮颜色", "正在唱的部分和弹出面板里的当前句", draft.HighlightColor, v => { draft.HighlightColor = v; Save(); }));
        Children.Add(textCard);

        var (lyrics, lyricsCard) = Card("歌词与行为");
        var offsetValue = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Width = 64, TextAlignment = TextAlignment.Right, Opacity = 0.75 };
        var offset = new Slider
        {
            Minimum = -3000, Maximum = 3000, StepFrequency = 100, SmallChange = 100, Value = draft.OffsetMs,
            Width = 150, VerticalAlignment = VerticalAlignment.Center,
        };
        offsetValue.Text = OffsetLabel(draft.OffsetMs);
        offset.ValueChanged += (_, e) =>
        {
            draft.OffsetMs = (int)e.NewValue;
            offsetValue.Text = OffsetLabel(draft.OffsetMs);
            Save();
        };
        var offsetPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        offsetPanel.Children.Add(offset);
        offsetPanel.Children.Add(offsetValue);
        lyrics.Children.Add(Row("歌词偏移", offsetPanel, "歌词比歌声慢就往右（提前）"));
        lyrics.Children.Add(ToggleRow("弹出面板显示翻译", draft.ShowTranslationInFlyout, v => { draft.ShowTranslationInFlyout = v; Save(); }, out _));
        lyrics.Children.Add(ToggleRow("没在播放时隐藏控件", draft.HideWhenIdle, v => { draft.HideWhenIdle = v; Save(); }, out _));

        var netease = new CheckBox { Content = "网易云", IsChecked = draft.UseNetease, MinWidth = 0 };
        netease.Click += (_, _) => { draft.UseNetease = netease.IsChecked == true; Save(); };
        var lrclib = new CheckBox { Content = "LRCLIB", IsChecked = draft.UseLrclib, MinWidth = 0 };
        lrclib.Click += (_, _) => { draft.UseLrclib = lrclib.IsChecked == true; Save(); };
        var sources = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        sources.Children.Add(netease);
        sources.Children.Add(lrclib);
        lyrics.Children.Add(Row("歌词来源", sources, "网易云中文歌更全，LRCLIB 英文歌更全"));
        Children.Add(lyricsCard);

        Enable(coverSizeRow, draft.ShowAlbumArt);
        Enable(titleSizeRow, draft.ShowTopLine);
        Enable(buttonSizeRow, draft.Controls != ControlsLayout.None);
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

    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) ? value as Brush : null;

    private static string OffsetLabel(int ms) => ms switch
    {
        0 => "歌词偏移：0（不调整）",
        > 0 => $"歌词偏移：提前 {ms / 1000.0:0.0} 秒",
        _ => $"歌词偏移：延后 {-ms / 1000.0:0.0} 秒",
    };

    /// <summary>A titled card: returns the panel rows go into, and the card to add to the page.</summary>
    private static (StackPanel Rows, UIElement Card) Card(string title)
    {
        var rows = new StackPanel();
        var outer = new StackPanel { Spacing = 4 };
        outer.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            Margin = new Thickness(2, 0, 0, 0),
        });
        outer.Children.Add(new Border
        {
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 2, 8, 2),
            Child = rows,
        });
        return (rows, outer);
    }

    /// <summary>Label (with optional hint as tooltip) on the left, control on the right; ~36px tall.</summary>
    private static Grid Row(string label, FrameworkElement control, string? hint = null)
    {
        var g = new Grid { MinHeight = 36, ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock
        {
            Text = label, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (hint is not null)
        {
            ToolTipService.SetToolTip(t, hint);
            t.Text = label + "  ⓘ";
        }
        g.Children.Add(t);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static Grid SliderRow(string label, string unit, int min, int max, int step, int value, Action<int> changed)
    {
        var valueText = new TextBlock
        {
            Text = $"{value}{unit}", Width = 52, TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75,
        };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, StepFrequency = step, SmallChange = step, Value = value,
            Width = 150, VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) =>
        {
            var v = (int)Math.Round(e.NewValue);
            valueText.Text = $"{v}{unit}";
            changed(v);
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        return Row(label, panel);
    }

    private static Grid ToggleRow(string label, bool value, Action<bool> changed, out ToggleSwitch toggle, string? hint = null)
    {
        var t = toggle = new ToggleSwitch
        {
            IsOn = value, OnContent = null, OffContent = null, MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        t.Toggled += (_, _) => changed(t.IsOn);
        return Row(label, t, hint);
    }

    private static void Enable(UIElement row, bool on)
    {
        row.Opacity = on ? 1.0 : 0.4;
        row.IsHitTestVisible = on;
    }

    private static readonly string[] Presets =
    {
        "#FFFFFF", "#000000", "#31C27C", "#E60026", "#FF6B81", "#FFB300", "#4FC3F7", "#7C4DFF",
    };

    /// <summary>
    /// A color setting: label, a swatch button that opens a full color picker with presets,
    /// and a "跟随系统" button that clears the custom color (back to the Windows theme).
    /// </summary>
    private static Grid ColorRow(string label, string description, string? current, Action<string?> changed)
    {
        var swatch = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x40, 0x80, 0x80, 0x80)),
        };
        var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        var buttonContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
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

        var reset = new HyperlinkButton { Content = "重置", Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(reset, "跟随系统颜色");
        reset.Click += (_, _) =>
        {
            suppress = true;
            picker.Color = Microsoft.UI.Colors.White;
            suppress = false;
            Show(null);
            changed(null);
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(button);
        controls.Children.Add(reset);
        return Row(label, controls, description);
    }
}

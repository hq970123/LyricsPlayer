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
            try
            {
                context.SaveSettings(draft.ToJson()); // also pushes the draft to the taskbar preview
                _sampleSlot.Width = draft.PreviewWidth;
                _sample.ApplySettings(draft);
                Tick();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"settings save failed: {ex}");
            }
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

        // ---- Which apps to follow: the apps that have sound, like the volume mixer.
        var (apps, appsCard) = Card("监听应用");
        var appList = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var appNote = new TextBlock { FontSize = 12, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
        apps.Children.Add(ToggleRow("只跟随指定应用", draft.FilterApps, v =>
        {
            draft.FilterApps = v; Enable(appList, v); UpdateAppNote(); Save();
        }, out _, "关闭时自动跟随系统当前的媒体会话；打开后只认勾选的应用，浏览器等其他来源会被忽略"));
        var refresh = new HyperlinkButton { Content = "刷新", Padding = new Thickness(6, 2, 6, 2) };
        apps.Children.Add(Row("有声音的应用", refresh, "和 Windows 音量合成器一样列出有音频会话的软件；勾选过但没在运行的也会保留"));
        apps.Children.Add(appList);
        apps.Children.Add(appNote);
        Children.Add(appsCard);

        string lastSignature = "";
        bool refreshing = false;

        async void RefreshApps(bool force = false)
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                var audio = await AudioSessions.GetAppsAsync();
                var media = mediaApps();
                var rows = new List<AppRow>();
                foreach (var a in audio)
                {
                    var hasMedia = media.Any(m => AppNames.Matches(a.Key, a.Aumid, m.AppId));
                    var state = a.IsSounding ? "正在发声" : a.IsActive ? "有声音会话" : "静默";
                    rows.Add(new AppRow(a.Key, a.Name, a.ExePath, a.Aumid, state, hasMedia, a.IsSounding));
                }
                // Media sessions whose app has no audio session right now (paused players).
                foreach (var m in media)
                {
                    if (rows.Any(r => AppNames.Matches(r.Key, r.Aumid, m.AppId))) continue;
                    var key = MediaKey(m.AppId);
                    if (rows.Any(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))) continue;
                    rows.Add(new AppRow(key, AppNames.Friendly(m.AppId), null, key.EndsWith(".exe") ? null : m.AppId,
                        m.IsPlaying ? "正在播放" : "已暂停", true, false));
                }
                // Selected apps that aren't running.
                foreach (var kv in draft.FollowApps)
                {
                    if (rows.Any(r => string.Equals(r.Key, kv.Key, StringComparison.OrdinalIgnoreCase))) continue;
                    rows.Add(new AppRow(kv.Key, kv.Value.Name.Length > 0 ? kv.Value.Name : AppNames.Friendly(kv.Key),
                        kv.Value.ExePath, kv.Value.Aumid, "未运行", false, false));
                }

                var signature = string.Join("|", rows.Select(r => $"{r.Key}:{r.State}:{r.HasMedia}"));
                if (!force && signature == lastSignature) return;
                lastSignature = signature;
                BuildAppList(rows);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"app list failed: {ex}");
            }
            finally
            {
                refreshing = false;
            }
        }

        void BuildAppList(List<AppRow> rows)
        {
            appList.Children.Clear();
            if (rows.Count == 0)
            {
                appList.Children.Add(new TextBlock { Text = "现在没有软件在使用声音。打开播放器播放一首歌，再点“刷新”。", FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap });
            }
            foreach (var r in rows)
            {
                var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var icon = new Image { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
                label.Children.Add(icon);
                label.Children.Add(new TextBlock { Text = r.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 });
                _ = LoadIconAsync(icon, r.ExePath);

                var box = new CheckBox
                {
                    IsChecked = draft.FollowApps.ContainsKey(r.Key), MinWidth = 0,
                    Content = label, VerticalAlignment = VerticalAlignment.Center,
                };
                ToolTipService.SetToolTip(box, r.Aumid is null ? r.Key : $"{r.Key}\n{r.Aumid}");
                var row = r;
                box.Click += (_, _) =>
                {
                    if (box.IsChecked == true)
                        draft.FollowApps[row.Key] = new FollowApp { Name = row.Name, Aumid = row.Aumid, ExePath = row.ExePath };
                    else draft.FollowApps.Remove(row.Key);
                    UpdateAppNote();
                    Save();
                };

                var stateText = new TextBlock
                {
                    Text = r.HasMedia || r.State == "未运行" ? r.State : r.State + " · 无媒体控制",
                    FontSize = 12, Opacity = r.IsSounding ? 0.9 : 0.55,
                };
                if (r.IsSounding && ThemeBrush("AccentTextFillColorPrimaryBrush") is { } accent) stateText.Foreground = accent;
                if (!r.HasMedia && r.State != "未运行")
                    ToolTipService.SetToolTip(stateText, "这个软件没有接入系统媒体控制（SMTC），控件拿不到它的歌名和进度；勾选后等它提供媒体信息时才会显示");
                appList.Children.Add(Relabel(Row("", box), box, stateText));
            }
            Enable(appList, draft.FilterApps);
            UpdateAppNote();
        }

        void UpdateAppNote()
        {
            appNote.Text = !draft.FilterApps
                ? "当前：自动，跟随系统正在播放的应用。"
                : draft.FollowApps.Count == 0
                    ? "还没有勾选应用，暂时仍按“自动”处理。"
                    : $"只跟随：{string.Join("、", draft.FollowApps.Values.Select(v => v.Name))}。";
        }

        IReadOnlyList<MediaAppInfo> mediaApps() => media?.GetSessionApps() ?? Array.Empty<MediaAppInfo>();

        refresh.Click += (_, _) => RefreshApps(force: true);
        // While the page is open, poll like the volume mixer does (cheap: a few COM calls).
        var appTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        appTimer.Tick += (_, _) => RefreshApps();
        EventHandler onSessions = (_, _) => RefreshApps();
        Loaded += (_, _) =>
        {
            if (media is not null) media.SessionsListChanged += onSessions;
            appTimer.Start();
            RefreshApps(force: true);
        };
        Unloaded += (_, _) =>
        {
            appTimer.Stop();
            if (media is not null) media.SessionsListChanged -= onSessions;
        };
        Enable(appList, draft.FilterApps);
        UpdateAppNote();

        Enable(coverSizeRow, draft.ShowAlbumArt);
        Enable(titleSizeRow, draft.ShowTopLine);
        Enable(buttonSizeRow, draft.Controls != ControlsLayout.None);
    }

    /// <summary>One frame of the sample: the real song when something is playing, otherwise demo lyrics.</summary>
    private void Tick()
    {
        try { TickCore(); }
        catch (Exception ex) { Debug.WriteLine($"settings preview failed: {ex}"); }
    }

    private void TickCore()
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

    private sealed record AppRow(string Key, string Name, string? ExePath, string? Aumid, string State, bool HasMedia, bool IsSounding);

    /// <summary>Settings key for an app known only by its media session id.</summary>
    private static string MediaKey(string aumid)
    {
        var id = aumid.Trim();
        var slash = id.LastIndexOfAny(new[] { '\\', '/' });
        var file = slash >= 0 ? id[(slash + 1)..] : id;
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return file.ToLowerInvariant();
        return id;
    }

    private static readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> IconCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The exe's icon via the shell thumbnail; left empty when Windows won't give one.</summary>
    private static async Task LoadIconAsync(Image target, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return;
        try
        {
            if (!IconCache.TryGetValue(exePath, out var bmp))
            {
                IconCache[exePath] = null;
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(exePath);
                using var thumb = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 32);
                if (thumb is null) return;
                bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 32 };
                await bmp.SetSourceAsync(thumb);
                IconCache[exePath] = bmp;
            }
            if (bmp is not null) target.Source = bmp;
        }
        catch { /* protected folders (WindowsApps) or a vanished exe */ }
    }

    /// <summary>Turns a Row into "checkbox (left) … state text (right)".</summary>
    private static Grid Relabel(Grid row, FrameworkElement left, FrameworkElement right)
    {
        row.Children.Clear();
        row.MinHeight = 32;
        left.VerticalAlignment = VerticalAlignment.Center;
        right.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
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
        "#FFFFFF", "#D9D9D9", "#8C8C8C", "#000000", "#31C27C", "#1DB954", "#00BFA5", "#4FC3F7",
        "#2979FF", "#7C4DFF", "#E040FB", "#FF6B81", "#E60026", "#FF7043", "#FFB300", "#FFEB3B",
    };

    /// <summary>
    /// A color setting: a swatch button opening a small picker (preset chips, hue / saturation /
    /// brightness sliders and a hex box), and "重置" which goes back to the Windows theme.
    /// Built from plain controls on purpose: WinUI's ColorPicker inside a Flyout is known to crash.
    /// Changes are saved after a short pause so dragging a slider doesn't flood WidBar with drafts.
    /// </summary>
    private static Grid ColorRow(string label, string description, string? current, Action<string?> changed)
    {
        var swatch = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x60, 0x80, 0x80, 0x80)),
        };
        var hexText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        var buttonContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttonContent.Children.Add(swatch);
        buttonContent.Children.Add(hexText);

        string? pending = current;
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            try { changed(pending); }
            catch (Exception ex) { Debug.WriteLine($"color save failed: {ex}"); }
        };

        void Show(string? hex)
        {
            var c = WidgetSettings.ParseColor(hex);
            swatch.Background = c is { } color ? new SolidColorBrush(color) : null;
            hexText.Text = c is null ? "跟随系统" : hex!.ToUpperInvariant();
        }
        Show(current);

        // ---- picker contents
        var start = WidgetSettings.ParseColor(current) ?? Microsoft.UI.Colors.White;
        RgbToHsv(start, out var h0, out var s0, out var v0);
        var big = new Border { Height = 28, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(start) };
        var hue = PickerSlider(0, 360, h0);
        var sat = PickerSlider(0, 100, s0 * 100);
        var val = PickerSlider(0, 100, v0 * 100);
        var hexBox = new TextBox { Text = WidgetSettings.ToHex(start), Width = 96, FontFamily = new FontFamily("Consolas"), MaxLength = 7 };
        bool syncing = false;

        void Commit(Windows.UI.Color c, bool fromHexBox = false)
        {
            var hex = WidgetSettings.ToHex(c);
            big.Background = new SolidColorBrush(c);
            if (!fromHexBox) hexBox.Text = hex;
            Show(hex);
            pending = hex;
            debounce.Stop();
            debounce.Start();
        }

        void FromSliders()
        {
            if (syncing) return;
            Commit(HsvToRgb(hue.Value, sat.Value / 100, val.Value / 100));
        }
        hue.ValueChanged += (_, _) => FromSliders();
        sat.ValueChanged += (_, _) => FromSliders();
        val.ValueChanged += (_, _) => FromSliders();

        void SetSliders(Windows.UI.Color c)
        {
            syncing = true;
            RgbToHsv(c, out var h, out var sv, out var vv);
            hue.Value = h; sat.Value = sv * 100; val.Value = vv * 100;
            syncing = false;
        }

        hexBox.TextChanged += (_, _) =>
        {
            if (syncing) return;
            var t = hexBox.Text.Trim();
            if (!t.StartsWith('#')) t = "#" + t;
            if (t.Length != 7 || WidgetSettings.ParseColor(t) is not { } c) return;
            SetSliders(c);
            Commit(c, fromHexBox: true);
        };

        var chips = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 8, ItemWidth = 28, ItemHeight = 28 };
        foreach (var p in Presets)
        {
            var color = WidgetSettings.ParseColor(p)!.Value;
            var chip = new Button
            {
                Width = 22, Height = 22, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x60, 0x80, 0x80, 0x80)),
            };
            ToolTipService.SetToolTip(chip, p);
            chip.Click += (_, _) => { SetSliders(color); Commit(color); };
            chips.Children.Add(chip);
        }

        var form = new StackPanel { Spacing = 6, Width = 240 };
        form.Children.Add(big);
        form.Children.Add(chips);
        form.Children.Add(Labeled("色相", hue));
        form.Children.Add(Labeled("饱和", sat));
        form.Children.Add(Labeled("亮度", val));
        form.Children.Add(Labeled("HEX", hexBox));

        var button = new DropDownButton { Content = buttonContent, Padding = new Thickness(8, 4, 8, 4), Flyout = new Flyout { Content = form } };

        var reset = new HyperlinkButton { Content = "重置", Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(reset, "跟随系统颜色");
        reset.Click += (_, _) =>
        {
            Show(null);
            pending = null;
            debounce.Stop();
            debounce.Start();
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(button);
        controls.Children.Add(reset);
        return Row(label, controls, description);
    }

    private static Slider PickerSlider(double min, double max, double value) => new()
    {
        Minimum = min, Maximum = max, StepFrequency = 1, Value = value, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Grid Labeled(string label, FrameworkElement control)
    {
        var g = new Grid { ColumnSpacing = 8 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 });
        control.HorizontalAlignment = control is Slider ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        Grid.SetColumn(control, 1);
        g.Children.Add(control);
        return g;
    }

    private static void RgbToHsv(Windows.UI.Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
    }

    private static Windows.UI.Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        static byte B(double f) => (byte)Math.Clamp(Math.Round(f * 255), 0, 255);
        return Windows.UI.Color.FromArgb(255, B(r + m), B(g + m), B(b + m));
    }
}

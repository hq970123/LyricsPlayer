using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidBar.SDK;

namespace LyricsPlayer.ExtensionApp.Views;

/// <summary>Settings page hosted by WidBar (it adds Save / Cancel).</summary>
public sealed class SettingsView : StackPanel
{
    public SettingsView(IWidgetSettingsContext context)
    {
        var draft = WidgetSettings.FromJson(context.SettingsJson);
        Spacing = 16;

        void Save()
        {
            context.SaveSettings(draft.ToJson());
            context.RequestPreviewRefresh();
        }

        Children.Add(Header("任务栏"));

        var width = new Slider
        {
            Header = $"宽度：{draft.PreviewWidth}px",
            Minimum = 200, Maximum = 720, StepFrequency = 10, Value = draft.PreviewWidth,
        };
        width.ValueChanged += (_, e) =>
        {
            draft.PreviewWidth = (int)e.NewValue;
            width.Header = $"宽度：{draft.PreviewWidth}px";
            Save();
        };
        Children.Add(width);

        Children.Add(Toggle("显示专辑封面", draft.ShowAlbumArt, v => { draft.ShowAlbumArt = v; Save(); }));
        Children.Add(Toggle("显示上一首/播放/下一首按钮", draft.ShowControls, v => { draft.ShowControls = v; Save(); }));
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

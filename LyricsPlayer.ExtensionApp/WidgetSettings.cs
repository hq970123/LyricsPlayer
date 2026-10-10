using System.Text.Json;

namespace LyricsPlayer.ExtensionApp;

/// <summary>Per-instance settings. WidBar stores this JSON for each placed copy.</summary>
public sealed class WidgetSettings
{
    /// <summary>Taskbar slot width in logical pixels.</summary>
    public int PreviewWidth { get; set; } = 420;
    public bool ShowAlbumArt { get; set; } = true;
    /// <summary>Old on/off switch for the buttons, kept so older saved settings still load. Use Controls.</summary>
    public bool ShowControls { get; set; } = true;
    /// <summary>Which playback buttons the taskbar shows.</summary>
    public ControlsLayout Controls { get; set; } = ControlsLayout.All;
    /// <summary>Album art edge, logical px.</summary>
    public int CoverSize { get; set; } = 36;
    /// <summary>Play/pause button diameter, logical px. Previous/next are 4px smaller.</summary>
    public int ButtonSize { get; set; } = 32;
    /// <summary>Gap between cover, text and buttons, logical px.</summary>
    public int Spacing { get; set; } = 8;
    /// <summary>Font size of the small "title - artist" line.</summary>
    public int TitleFontSize { get; set; } = 12;
    /// <summary>Font size of the lyric line (and the song name when there are no lyrics).</summary>
    public int LyricFontSize { get; set; } = 14;
    /// <summary>Show the small title line above the lyric. Off gives one bigger lyric line.</summary>
    public bool ShowTopLine { get; set; } = true;
    /// <summary>When the song has a translation, show it on the small top line instead of title-artist.</summary>
    public bool TranslationOnTopLine { get; set; }
    public bool ShowTranslationInFlyout { get; set; } = true;
    /// <summary>Positive shows lyrics earlier, negative later.</summary>
    public int OffsetMs { get; set; }
    public bool UseNetease { get; set; } = true;
    public bool UseLrclib { get; set; } = true;
    /// <summary>Text color for title, artist and not-yet-sung lyrics, as #RRGGBB. Null follows the Windows theme.</summary>
    public string? TextColor { get; set; }
    /// <summary>Color of the sung part of the lyric and the current line in the flyout. Null follows the theme.</summary>
    public string? HighlightColor { get; set; }
    /// <summary>Scroll the song name when it is too long for its slot.</summary>
    public bool ScrollLongTitle { get; set; } = true;
    /// <summary>Remove the widget from the taskbar when nothing is playing.</summary>
    public bool HideWhenIdle { get; set; }

    public const int MinWidth = 160;
    public const int MaxWidth = 800;

    public static WidgetSettings FromJson(string? json)
    {
        try
        {
            var s = string.IsNullOrWhiteSpace(json)
                ? new WidgetSettings()
                : JsonSerializer.Deserialize<WidgetSettings>(json) ?? new WidgetSettings();
            s.PreviewWidth = Math.Clamp(s.PreviewWidth, MinWidth, MaxWidth);
            if (!s.ShowControls)
            {
                // Settings saved before Controls existed.
                s.Controls = ControlsLayout.None;
                s.ShowControls = true;
            }
            if (!Enum.IsDefined(s.Controls)) s.Controls = ControlsLayout.All;
            s.CoverSize = Math.Clamp(s.CoverSize, 20, 48);
            s.ButtonSize = Math.Clamp(s.ButtonSize, 24, 40);
            s.Spacing = Math.Clamp(s.Spacing, 0, 20);
            s.TitleFontSize = Math.Clamp(s.TitleFontSize, 9, 20);
            s.LyricFontSize = Math.Clamp(s.LyricFontSize, 10, 26);
            s.OffsetMs = Math.Clamp(s.OffsetMs, -5000, 5000);
            return s;
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public static Windows.UI.Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        try
        {
            if (h.Length == 6) h = "FF" + h;
            if (h.Length != 8) return null;
            var v = Convert.ToUInt32(h, 16);
            return Windows.UI.Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
        catch
        {
            return null;
        }
    }

    public static string ToHex(Windows.UI.Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    public Microsoft.UI.Xaml.Media.Brush? TextBrush =>
        ParseColor(TextColor) is { } c ? new Microsoft.UI.Xaml.Media.SolidColorBrush(c) : null;

    public Microsoft.UI.Xaml.Media.Brush? HighlightBrush =>
        ParseColor(HighlightColor) is { } c ? new Microsoft.UI.Xaml.Media.SolidColorBrush(c) : null;

    public string ToJson() => JsonSerializer.Serialize(this);

    public WidgetSettings Clone() => FromJson(ToJson());
}

public enum ControlsLayout
{
    /// <summary>Previous, play/pause, next.</summary>
    All,
    /// <summary>Only play/pause, leaves more room for the text.</summary>
    PlayPauseOnly,
    None,
}

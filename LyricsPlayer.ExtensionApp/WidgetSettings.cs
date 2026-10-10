using System.Text.Json;

namespace LyricsPlayer.ExtensionApp;

/// <summary>Per-instance settings. WidBar stores this JSON for each placed copy.</summary>
public sealed class WidgetSettings
{
    /// <summary>Taskbar slot width in logical pixels.</summary>
    public int PreviewWidth { get; set; } = 420;
    public bool ShowAlbumArt { get; set; } = true;
    public bool ShowControls { get; set; } = true;
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

    public static WidgetSettings FromJson(string? json)
    {
        try
        {
            var s = string.IsNullOrWhiteSpace(json)
                ? new WidgetSettings()
                : JsonSerializer.Deserialize<WidgetSettings>(json) ?? new WidgetSettings();
            s.PreviewWidth = Math.Clamp(s.PreviewWidth, 200, 720);
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
}

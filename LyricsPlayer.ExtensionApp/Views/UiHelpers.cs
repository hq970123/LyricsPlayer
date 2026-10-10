using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace LyricsPlayer.ExtensionApp.Views;

internal static class UiHelpers
{
    public const string GlyphPrevious = "\uE892";
    public const string GlyphNext = "\uE893";
    public const string GlyphPlay = "\uE768";
    public const string GlyphPause = "\uE769";
    public const string GlyphMusic = "\uE8D6";
    public const string GlyphRefresh = "\uE72C";

    /// <summary>A borderless icon button that keeps the theme's hover and pressed states.</summary>
    public static Button IconButton(string glyph, double size, double iconSize, string tooltip, bool filled = false)
    {
        var button = new Button
        {
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(size / 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = glyph, FontSize = iconSize },
        };
        var clear = new SolidColorBrush(Colors.Transparent);
        button.Resources["ButtonBorderBrush"] = clear;
        button.Resources["ButtonBorderBrushPointerOver"] = clear;
        button.Resources["ButtonBorderBrushPressed"] = clear;
        if (!filled)
        {
            button.Resources["ButtonBackground"] = clear;
        }
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    public static void SetGlyph(Button button, string glyph)
    {
        if (button.Content is FontIcon icon && icon.Glyph != glyph) icon.Glyph = glyph;
    }

    public static async Task<ImageSource?> ToImageAsync(byte[]? bytes, int decodeSize)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);
            var bmp = new BitmapImage { DecodePixelWidth = decodeSize, DecodePixelType = DecodePixelType.Logical };
            await bmp.SetSourceAsync(stream);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    /// <summary>Turns an AppUserModelId into a name people recognise.</summary>
    public static string FriendlyAppName(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return "";
        var id = appId.ToLowerInvariant();
        if (id.Contains("cloudmusic")) return "网易云音乐";
        if (id.Contains("qqmusic")) return "QQ音乐";
        if (id.Contains("kugou")) return "酷狗音乐";
        if (id.Contains("kuwo")) return "酷我音乐";
        if (id.Contains("spotify")) return "Spotify";
        if (id.Contains("applemusic") || id.Contains("apple")) return "Apple Music";
        if (id.Contains("zunemusic") || id.Contains("media player")) return "媒体播放器";
        if (id.Contains("msedge")) return "Edge";
        if (id.Contains("chrome")) return "Chrome";
        if (id.Contains("firefox")) return "Firefox";
        if (id.Contains("foobar")) return "foobar2000";
        var name = appId.Contains('!') ? appId[(appId.LastIndexOf('!') + 1)..] : appId;
        return Path.GetFileNameWithoutExtension(name);
    }
}

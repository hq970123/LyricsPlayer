using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace LyricsPlayer.ExtensionApp.Views;

/// <summary>
/// Single-line text that scrolls back and forth when it is wider than its slot:
/// hold at the start, glide to the end, hold, jump back. Short text just sits still.
/// </summary>
public sealed class MarqueeText : Grid
{
    private const double PixelsPerSecond = 30;
    private static readonly TimeSpan HoldStart = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HoldEnd = TimeSpan.FromSeconds(1.5);

    private readonly Canvas _canvas = new();
    private readonly TextBlock _text;
    private readonly TranslateTransform _shift = new();
    private readonly RectangleGeometry _clip = new();
    private Storyboard? _story;
    private double _runningOverflow = -1;
    private bool _scrollEnabled = true;

    public MarqueeText(double fontSize)
    {
        _text = new TextBlock
        {
            FontSize = fontSize,
            TextWrapping = TextWrapping.NoWrap,
            RenderTransform = _shift,
        };
        _canvas.Height = Math.Ceiling(fontSize * 1.35);
        _canvas.Children.Add(_text);
        Children.Add(_canvas);
        Clip = _clip;

        SizeChanged += (_, _) => Recompute();
        _text.SizeChanged += (_, _) =>
        {
            _canvas.Height = _text.ActualHeight;
            Recompute();
        };
    }

    public string Text
    {
        get => _text.Text;
        set
        {
            if (_text.Text == value) return;
            _text.Text = value;
            _runningOverflow = -1; // restart from the beginning for new text
            Recompute();
        }
    }

    public TextBlock Inner => _text;

    public Brush? Foreground
    {
        set
        {
            if (value is null) _text.ClearValue(TextBlock.ForegroundProperty);
            else _text.Foreground = value;
        }
    }

    /// <summary>Turn scrolling off (setting) or pause it (widget hidden). Long text is then trimmed.</summary>
    public bool ScrollEnabled
    {
        get => _scrollEnabled;
        set
        {
            if (_scrollEnabled == value) return;
            _scrollEnabled = value;
            _runningOverflow = -1;
            Recompute();
        }
    }

    private void Recompute()
    {
        var available = ActualWidth;
        _clip.Rect = new Rect(0, 0, Math.Max(0, available), Math.Max(ActualHeight, _canvas.Height));
        if (available <= 0) return;

        var overflow = _text.ActualWidth - available;
        if (!_scrollEnabled || overflow <= 1)
        {
            Stop();
            // Without scrolling, fall back to an ellipsis inside the slot.
            _text.MaxWidth = _scrollEnabled ? double.PositiveInfinity : available;
            _text.TextTrimming = _scrollEnabled ? TextTrimming.None : TextTrimming.CharacterEllipsis;
            return;
        }

        _text.MaxWidth = double.PositiveInfinity;
        _text.TextTrimming = TextTrimming.None;
        if (Math.Abs(overflow - _runningOverflow) < 1) return; // already animating this distance
        Start(overflow);
    }

    private void Start(double overflow)
    {
        Stop();
        _runningOverflow = overflow;

        var travel = TimeSpan.FromSeconds(overflow / PixelsPerSecond);
        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 0 });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(HoldStart), Value = 0 });
        anim.KeyFrames.Add(new LinearDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(HoldStart + travel), Value = -overflow });
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(HoldStart + travel + HoldEnd), Value = -overflow });

        Storyboard.SetTarget(anim, _shift);
        Storyboard.SetTargetProperty(anim, "X");
        _story = new Storyboard();
        _story.Children.Add(anim);
        _story.Begin();
    }

    private void Stop()
    {
        _story?.Stop();
        _story = null;
        _runningOverflow = -1;
        _shift.X = 0;
    }
}

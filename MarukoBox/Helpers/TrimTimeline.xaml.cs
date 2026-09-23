using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MarukoBox.Helpers;

/// <summary>时间轴上正在被拖动的元素。</summary>
public enum TrimHandle
{
    /// <summary>裁剪起点</summary>
    Start,

    /// <summary>裁剪终点</summary>
    End,

    /// <summary>播放位置指针</summary>
    Playhead
}

/// <summary>时间轴一次编辑事件的参数。</summary>
public sealed class TrimEditEventArgs : EventArgs
{
    /// <summary>被操作的元素。</summary>
    public TrimHandle Handle { get; init; }

    /// <summary>是否仍在拖动中（false 表示单击定位这一类的离散操作）。</summary>
    public bool IsDragging { get; init; }
}

/// <summary>
/// 裁剪时间轴：一条轨道 + 左右两个可拖动的区间端点 + 播放位置指针。
///
/// 为什么不用两个 Slider 拼：Slider 的 Thumb 命中区小、两端会互相穿插，
/// 而且无法表达"选中区间"这层语义。这里用 Canvas 自己摆位，
/// 端点按 x 距离就近命中，拖动时先移动、松手才通知外部刷新缩略图。
/// </summary>
public sealed partial class TrimTimeline : UserControl
{
    /// <summary>两端之间允许的最短间隔（秒），避免出现零长度片段。</summary>
    private const double MinGapSeconds = 0.1;

    /// <summary>端点的命中半径（像素）：手指不必精确压在 14px 的握把上。</summary>
    private const double HitSlop = 16;

    private TrimHandle? _dragging;
    private bool _hasPointerCapture;

    public TrimTimeline()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Layout();
        Loaded += (_, _) => Layout();
    }

    // ---------- 依赖属性：让页面可以绑定 / 直接赋值 ----------

    /// <summary>视频总时长。</summary>
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
        nameof(Duration), typeof(TimeSpan), typeof(TrimTimeline),
        new PropertyMetadata(TimeSpan.Zero, OnRangePropertyChanged));

    /// <summary>裁剪起点。</summary>
    public static readonly DependencyProperty StartProperty = DependencyProperty.Register(
        nameof(Start), typeof(TimeSpan), typeof(TrimTimeline),
        new PropertyMetadata(TimeSpan.Zero, OnRangePropertyChanged));

    /// <summary>裁剪终点。</summary>
    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(TimeSpan), typeof(TrimTimeline),
        new PropertyMetadata(TimeSpan.Zero, OnRangePropertyChanged));

    /// <summary>当前播放位置。</summary>
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(TimeSpan), typeof(TrimTimeline),
        new PropertyMetadata(TimeSpan.Zero, OnRangePropertyChanged));

    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public TimeSpan Start
    {
        get => (TimeSpan)GetValue(StartProperty);
        set => SetValue(StartProperty, value);
    }

    public TimeSpan End
    {
        get => (TimeSpan)GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }

    public TimeSpan Position
    {
        get => (TimeSpan)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    private static void OnRangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrimTimeline self)
        {
            self.Layout();
        }
    }

    /// <summary>拖动过程中持续触发（用于实时 seek 与刷新时间文字）。</summary>
    public event EventHandler<TrimEditEventArgs>? Edited;

    /// <summary>一次拖动结束（松手）时触发，用于触发缩略图这类较重的刷新。</summary>
    public event EventHandler? Committed;

    // ---------- 坐标换算 ----------

    private double TrackWidth => Track.ActualWidth;

    /// <summary>
    /// 时间 → x 坐标。两端各留出半个握把的余量，
    /// 否则起点在 0、终点在总时长时握把会有一半落在控件外面（被裁掉）。
    /// </summary>
    private double TimeToX(TimeSpan t)
    {
        var usable = UsableWidth;
        var total = Duration.TotalSeconds;
        if (total <= 0 || usable <= 0)
        {
            return ThumbHalf;
        }

        var ratio = Math.Clamp(t.TotalSeconds / total, 0, 1);
        return ThumbHalf + (ratio * usable);
    }

    private TimeSpan XToTime(double x)
    {
        var usable = UsableWidth;
        var total = Duration.TotalSeconds;
        if (total <= 0 || usable <= 0)
        {
            return TimeSpan.Zero;
        }

        var ratio = Math.Clamp((x - ThumbHalf) / usable, 0, 1);
        return TimeSpan.FromSeconds(ratio * total);
    }

    /// <summary>握把半宽：既是端点居中所需的偏移，也是轨道两端的留白。</summary>
    private const double ThumbHalf = 7;

    /// <summary>去掉两端留白后可用于映射时间的宽度。</summary>
    private double UsableWidth => TrackWidth - (ThumbHalf * 2);

    /// <summary>按当前时间值重排所有元素的位置。</summary>
    private void Layout()
    {
        var w = TrackWidth;
        if (w <= 0)
        {
            return;
        }

        const double barTop = 13;
        const double barHeight = 8;

        Canvas.SetLeft(TrackBase, 0);
        Canvas.SetTop(TrackBase, barTop);
        TrackBase.Width = w;
        TrackBase.Height = barHeight;

        var x1 = TimeToX(Start);
        var x2 = TimeToX(End);

        Canvas.SetLeft(SelectionRect, x1);
        Canvas.SetTop(SelectionRect, barTop);
        SelectionRect.Width = Math.Max(0, x2 - x1);
        SelectionRect.Height = barHeight;

        // 遮罩：轨道上未选中的部分压暗
        Canvas.SetLeft(DimLeft, 0);
        Canvas.SetTop(DimLeft, barTop);
        DimLeft.Width = Math.Max(0, x1);
        DimLeft.Height = barHeight;

        Canvas.SetLeft(DimRight, x2);
        Canvas.SetTop(DimRight, barTop);
        DimRight.Width = Math.Max(0, w - x2);
        DimRight.Height = barHeight;

        // 端点握把：以中心对齐到时间刻度，所以要左移半个宽度
        Canvas.SetLeft(ThumbStart, x1 - ThumbHalf);
        Canvas.SetTop(ThumbStart, 3);

        Canvas.SetLeft(ThumbEnd, x2 - ThumbHalf);
        Canvas.SetTop(ThumbEnd, 3);

        Canvas.SetLeft(Playhead, TimeToX(Position) - 1);
        Canvas.SetTop(Playhead, 2);
    }

    // ---------- 指针交互 ----------

    private void Track_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (Duration <= TimeSpan.Zero)
        {
            return;
        }

        var x = e.GetCurrentPoint(Track).Position.X;
        var x1 = TimeToX(Start);
        var x2 = TimeToX(End);

        // 就近命中：两个端点都比指针更近时，取更近的那个（避免区间很窄时永远抓到起点）
        var dStart = Math.Abs(x - x1);
        var dEnd = Math.Abs(x - x2);

        _dragging = (dStart <= HitSlop || dEnd <= HitSlop)
            ? (dStart <= dEnd ? TrimHandle.Start : TrimHandle.End)
            : TrimHandle.Playhead;

        _hasPointerCapture = Track.CapturePointer(e.Pointer);

        // 单击空白处 = 直接把播放位置挪过去
        ApplyPointer(x, isDragging: true);
        ShowBubble(x);

        e.Handled = true;
    }

    private void Track_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null)
        {
            return;
        }

        var x = e.GetCurrentPoint(Track).Position.X;
        ApplyPointer(x, isDragging: true);
        ShowBubble(x);
        e.Handled = true;
    }

    private void Track_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging is null)
        {
            return;
        }

        if (_hasPointerCapture)
        {
            Track.ReleasePointerCapture(e.Pointer);
            _hasPointerCapture = false;
        }

        var handle = _dragging.Value;
        _dragging = null;
        HideBubble();
        Layout();

        Committed?.Invoke(this, EventArgs.Empty);
        _ = handle; // 保留供将来按端点区分行为
        e.Handled = true;
    }

    /// <summary>把指针位置换算成时间并写回对应属性；同时保证区间约束成立。</summary>
    private void ApplyPointer(double x, bool isDragging)
    {
        var t = XToTime(x);
        var handle = _dragging ?? TrimHandle.Playhead;

        switch (handle)
        {
            case TrimHandle.Start:
            {
                var max = End.TotalSeconds - MinGapSeconds;
                var sec = Math.Clamp(t.TotalSeconds, 0, Math.Max(0, max));
                Start = TimeSpan.FromSeconds(sec);
                break;
            }

            case TrimHandle.End:
            {
                var min = Start.TotalSeconds + MinGapSeconds;
                var sec = Math.Clamp(t.TotalSeconds, Math.Min(Duration.TotalSeconds, min), Duration.TotalSeconds);
                End = TimeSpan.FromSeconds(sec);
                break;
            }

            default:
            {
                Position = t;
                break;
            }
        }

        Edited?.Invoke(this, new TrimEditEventArgs { Handle = handle, IsDragging = isDragging });
        Layout();
    }

    // ---------- 拖动时跟随的时间气泡 ----------

    private void ShowBubble(double x)
    {
        if (_dragging is null)
        {
            return;
        }

        var t = _dragging switch
        {
            TrimHandle.Start => Start,
            TrimHandle.End => End,
            _ => Position
        };

        BubbleText.Text = FormatClock(t);
        Bubble.Visibility = Visibility.Visible;

        // 气泡跟随指针，但夹在轨道范围内，避免超出边界被裁掉
        var left = Math.Clamp(x - 28, 0, Math.Max(0, TrackWidth - 56));
        Bubble.Margin = new Thickness(left, 0, 0, 2);
    }

    private void HideBubble()
    {
        Bubble.Visibility = Visibility.Collapsed;
    }

    /// <summary>把时间格式化成 mm:ss.fff（超过 1 小时时补上小时位）。</summary>
    public static string FormatClock(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
        {
            t = TimeSpan.Zero;
        }

        return t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : t.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }
}

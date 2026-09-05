namespace MarukoBox.Models;

/// <summary>
/// 视频抽帧模式。
/// </summary>
public enum FrameMode
{
    /// <summary>单张截图（指定时间点）。</summary>
    Single,

    /// <summary>每隔固定间隔抽取一帧（序列帧）。</summary>
    Interval
}

/// <summary>
/// 「图片」页视频抽帧的参数集合（一次性选项，不绑定动态属性）。
/// </summary>
public class FrameExtractOptions
{
    /// <summary>抽帧模式。</summary>
    public FrameMode Mode { get; set; } = FrameMode.Single;

    /// <summary>单张模式下的截图时间（秒）。</summary>
    public double TimeSeconds { get; set; } = 0;

    /// <summary>间隔模式下的抽帧间隔（秒）。</summary>
    public double IntervalSeconds { get; set; } = 5;

    /// <summary>输出图片格式（png / jpg）。</summary>
    public string Format { get; set; } = "png";

    /// <summary>缩放宽度（0 表示不缩放）。</summary>
    public int ScaleWidth { get; set; }

    /// <summary>缩放高度（0 表示不缩放）。</summary>
    public int ScaleHeight { get; set; }
}

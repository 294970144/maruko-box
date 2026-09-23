using System;

namespace MarukoBox.Models;

/// <summary>
/// 裁剪模式：决定"切得准"还是"切得快"。
/// </summary>
public enum TrimMode
{
    /// <summary>精确剪切：重新编码视频轨，切点精确到帧，耗时取决于片段长度与编码器。</summary>
    ReEncode,

    /// <summary>快速剪切：直接拷贝码流（-c copy），秒级完成且无损，但切点对齐最近关键帧。</summary>
    Copy
}

/// <summary>
/// 一次裁剪任务的参数。
/// </summary>
public class TrimRequest
{
    /// <summary>源视频路径。</summary>
    public string InputPath { get; set; } = string.Empty;

    /// <summary>输出文件路径（含扩展名）。</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>裁剪起点（相对源视频 0 时刻）。</summary>
    public TimeSpan Start { get; set; } = TimeSpan.Zero;

    /// <summary>裁剪终点（相对源视频 0 时刻，不含）。</summary>
    public TimeSpan End { get; set; } = TimeSpan.Zero;

    /// <summary>裁剪模式。</summary>
    public TrimMode Mode { get; set; } = TrimMode.ReEncode;

    /// <summary>精确模式使用的编码器；<see cref="EncoderType.Auto"/> 时按本机硬件解析。</summary>
    public EncoderType Encoder { get; set; } = EncoderType.Auto;

    /// <summary>质量档位：NVENC 走 -cq，CPU 编码器走 -crf。数值越小质量越高。</summary>
    public int Quality { get; set; } = 20;

    /// <summary>片段时长（由 End - Start 得出，最小 0）。</summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}

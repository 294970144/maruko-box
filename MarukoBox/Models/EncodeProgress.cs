using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarukoBox.Models;

/// <summary>
/// 编码任务的实时进度信息。
/// 由 <c>FfmpegService</c> 解析 ffmpeg 输出后填充，供 UI 绑定。
/// 采用 partial property 语法以满足 WinUI 3 / WinRT 的 AOT 与编组要求。
/// </summary>
public partial class EncodeProgress : ObservableObject
{
    /// <summary>完成百分比，0 ~ 100。</summary>
    [ObservableProperty]
    public partial double Percent { get; set; }

    /// <summary>编码速度倍率，例如 8.7 表示 8.7 倍速。</summary>
    [ObservableProperty]
    public partial double Speed { get; set; }

    /// <summary>当前处理帧率 (fps)。</summary>
    [ObservableProperty]
    public partial double Fps { get; set; }

    /// <summary>已处理时长。</summary>
    [ObservableProperty]
    public partial TimeSpan Processed { get; set; }

    /// <summary>预计剩余时长。</summary>
    [ObservableProperty]
    public partial TimeSpan Remaining { get; set; }

    /// <summary>当前处理的帧号。</summary>
    [ObservableProperty]
    public partial long CurrentFrame { get; set; }

    /// <summary>总帧数（未知时为 0）。</summary>
    [ObservableProperty]
    public partial long TotalFrames { get; set; }

    /// <summary>当前输出码率 (kbps)。</summary>
    [ObservableProperty]
    public partial double BitrateKbps { get; set; }

    /// <summary>正在处理的文件名。</summary>
    [ObservableProperty]
    public partial string CurrentFile { get; set; } = string.Empty;

    /// <summary>状态描述文本（如「编码中」「已完成」「已取消」）。</summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    /// <summary>是否已完成（成功或失败）。</summary>
    [ObservableProperty]
    public partial bool IsCompleted { get; set; }

    /// <summary>是否出错。</summary>
    [ObservableProperty]
    public partial bool HasError { get; set; }

    /// <summary>错误信息。</summary>
    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace MarukoBox.Models;

/// <summary>
/// 媒体流类型（来自 ffmpeg 的 Stream 行解析）。
/// </summary>
public enum StreamType
{
    /// <summary>未知 / 其它。</summary>
    Unknown,

    /// <summary>视频流。</summary>
    Video,

    /// <summary>音频流。</summary>
    Audio,

    /// <summary>字幕流。</summary>
    Subtitle,

    /// <summary>数据流（如章节、字体）。</summary>
    Data,

    /// <summary>附件流（如内嵌字体）。</summary>
    Attachment
}

/// <summary>
/// 「抽取」页中表示源文件里的一条可抽取轨道。
/// 绑定到轨道列表的复选框与色标。
/// 采用 partial property 语法以满足 WinUI 3 / WinRT 的 AOT 与编组要求。
/// </summary>
public partial class MediaStreamInfo : ObservableObject
{
    /// <summary>流序号（ffmpeg 中的 0:x 的 x）。</summary>
    [ObservableProperty]
    public partial int Index { get; set; }

    /// <summary>流类型。</summary>
    [ObservableProperty]
    public partial StreamType Type { get; set; }

    /// <summary>编码格式，例如 hevc / aac / ass。</summary>
    [ObservableProperty]
    public partial string Codec { get; set; } = string.Empty;

    /// <summary>语言代码，例如 eng / chi（无则为空）。</summary>
    [ObservableProperty]
    public partial string Language { get; set; } = string.Empty;

    /// <summary>ffmpeg 输出的完整描述（分辨率 / 采样率 / 声道等）。</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    /// <summary>是否勾选抽取（绑定到列表复选框）。</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>类型的中文标签，供 UI 展示。</summary>
    public string TypeLabel => Type switch
    {
        StreamType.Video => "视频",
        StreamType.Audio => "音频",
        StreamType.Subtitle => "字幕",
        StreamType.Data => "数据",
        StreamType.Attachment => "附件",
        _ => "未知"
    };
}

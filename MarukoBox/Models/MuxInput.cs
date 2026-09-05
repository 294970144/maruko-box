using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarukoBox.Models;

/// <summary>
/// 「封装」页中参与合并的输入条目类型（按扩展名推断）。
/// </summary>
public enum MuxKind
{
    /// <summary>视频文件（含其自带音视频流）。</summary>
    Video,

    /// <summary>独立音轨文件。</summary>
    Audio,

    /// <summary>独立字幕文件。</summary>
    Subtitle
}

/// <summary>
/// 「封装」页中表示一条待合并的输入（视频 / 音频 / 字幕文件）。
/// 类型按扩展名推断，绑定到轨道列表展示。
/// 采用 partial property 语法以满足 WinUI 3 / WinRT 的 AOT 与编组要求。
/// </summary>
public partial class MuxInput : ObservableObject
{
    /// <summary>用文件路径构造，构造时即推断类型。</summary>
    public MuxInput(string path)
    {
        FilePath = path;
        Kind = InferKind(path);
    }

    /// <summary>文件路径（绑定到列表「源文件」列）。</summary>
    [ObservableProperty]
    public partial string FilePath { get; set; }

    /// <summary>推断出的条目类型（视频 / 音频 / 字幕）。</summary>
    public MuxKind Kind { get; }

    /// <summary>文件名（不含路径），用于展示。</summary>
    public string FileName => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);

    /// <summary>类型中文标签。</summary>
    public string KindLabel => Kind switch
    {
        MuxKind.Video => "视频",
        MuxKind.Audio => "音频",
        MuxKind.Subtitle => "字幕",
        _ => "未知"
    };

    /// <summary>按扩展名推断输入类型。</summary>
    private static MuxKind InferKind(string path)
    {
        var ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" or ".flv" or ".wmv" or ".m4v"
                or ".ts" or ".m2ts" or ".mpg" or ".mpeg" => MuxKind.Video,
            ".m4a" or ".mp3" or ".opus" or ".flac" or ".wav" or ".aac" or ".eac3" or ".ac3"
                or ".dts" or ".thd" or ".ogg" or ".mka" => MuxKind.Audio,
            ".srt" or ".ass" or ".ssa" or ".sub" or ".idx" or ".sup" or ".vtt" or ".smi" or ".scc" => MuxKind.Subtitle,
            _ => MuxKind.Video
        };
    }
}

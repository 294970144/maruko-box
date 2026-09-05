using System.Collections.Generic;

namespace MarukoBox.Models;

/// <summary>
/// 「工具」页媒体信息查看的结果：总时长 + 流列表。
/// </summary>
public class MediaFileInfo
{
    /// <summary>总时长（无法解析时为 Zero）。</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>解析出的媒体流列表。</summary>
    public List<MediaStreamInfo> Streams { get; set; } = new();

    /// <summary>时长文本（hh:mm:ss.ff），无则为「—」。</summary>
    public string DurationText => Duration > TimeSpan.Zero
        ? Duration.ToString(@"hh\:mm\:ss\.ff")
        : "—";

    /// <summary>流数量文本。</summary>
    public string StreamCountText => $"{Streams.Count} 条流";
}
